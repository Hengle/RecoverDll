using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace HackDll
{
    internal class Program
    {
        private static string mDirectorPath;

        private static int BitCount(long n)
        {
            long c;
            for (c = 0; n != 0; n >>= 1)
            {
                c += n & 1;
            }
            return (int)c;
        }

        private static void CheckBuff(byte[] bytes, List<long> posList, int byteCount, long startPos)
        {
            for (int i = 0; i < byteCount; i++)
            {
                if (bytes[i] == 0x4d)
                {
                    if (i + 1 < byteCount && bytes[i + 1] == 0x5a)
                    {
                        posList.Add(i + startPos);
                    }
                }
            }
        }

        private static int GetDiskAddress(List<SectionHeader> sectionHeaders, int virtualAddress)
        {
            int diskAddress = 0;
            for (int i = sectionHeaders.Count - 1; i >= 0; i--)
            {
                if (virtualAddress > sectionHeaders[i].mVirtualAddress)
                {
                    diskAddress = virtualAddress - sectionHeaders[i].mVirtualAddress +
                                  sectionHeaders[i].mDiskAddress;
                    break;
                }
            }
            return diskAddress;
        }

        [STAThread]
        private static void Main()
        {
            string path = Application.StartupPath;
            Console.WriteLine(path);
            CommonOpenFileDialog commonOpenFileDialog = new CommonOpenFileDialog();
            commonOpenFileDialog.IsFolderPicker = true;
            commonOpenFileDialog.Title = "select directory";
            commonOpenFileDialog.RestoreDirectory = false;
            CommonFileDialogResult result = commonOpenFileDialog.ShowDialog();
            if (result == CommonFileDialogResult.Ok)
            {
                int bufferSize = 1024000;
                byte[] buffer = new byte[bufferSize];
                DirectoryInfo directory = new DirectoryInfo(commonOpenFileDialog.FileName);
                FileInfo[] files = directory.GetFiles("*.*");
                mDirectorPath = commonOpenFileDialog.FileName;
                foreach (FileInfo fileInfo in files)
                {
                    FileStream fs = File.OpenRead(fileInfo.FullName);
                    int totalCount = (int)(fs.Length / bufferSize);
                    List<long> posList = new List<long>();
                    for (int i = 0; i < totalCount; i++)
                    {
                        fs.Read(buffer, 0, bufferSize);
                        CheckBuff(buffer, posList, bufferSize, i * bufferSize);
                    }
                    int left = (int)(fs.Length - bufferSize * totalCount);
                    if (left > 0)
                    {
                        fs.Read(buffer, 0, left);
                        CheckBuff(buffer, posList, left, totalCount * bufferSize);
                    }
                    if (posList.Count == 0)
                    {
                        fs.Close();
                        continue;
                    }
                    //Console.WriteLine("{0} :{1}", fileInfo.Name, posList.Count);
                    BinaryReader br = new BinaryReader(fs);
                    for (int i = 0; i < posList.Count; i++)
                    {
                        fs.Position = posList[i];
                        ReadDll(br);
                    }
                    br.Dispose();
                    fs.Close();
                }
            }

            //Console.ReadKey(false);
        }

        private static void ReadDll(BinaryReader br)
        {
            long dllBegin = br.BaseStream.Position;
            int cb = br.ReadInt32();
            //DOS Header
            if (cb == 0x905A4D)
            {
                br.BaseStream.Position = dllBegin + 0x3c;
                int pePos = br.ReadInt32();
                br.BaseStream.Position = dllBegin + pePos;

                //NT Header
                int peSignature = br.ReadInt32();
                if (peSignature == 0x4550)
                {
                    br.BaseStream.Position += 2;
                    int sectionCount = br.ReadInt16();
                    br.BaseStream.Position += 12;
                    short sizeOfOptionalHeader = br.ReadInt16();
                    //Console.WriteLine(sizeOfOptionalHeader);
                    br.BaseStream.Position += 2;
                    long peIndex = br.BaseStream.Position;

                    //OptionalHeader
                    short magic = br.ReadInt16();
                    if (magic == 267)
                    {
                        //Console.WriteLine("is dll");
                    }
                    br.BaseStream.Position += 26;
                    br.BaseStream.Position += 8;
                    /*int fileAlignment = */
                    br.ReadInt32();
                    br.BaseStream.Position += 16;
                    /*int sizeOfImage = */
                    br.ReadInt32();
                    /*int sizeOfHeaders = */
                    br.ReadInt32();
                    br.BaseStream.Position += 72;
                    //dll size
                    int total = br.ReadInt32() + br.ReadInt32() + 152;
                    if (total > br.BaseStream.Length)
                    {
                        return;
                    }
                    br.BaseStream.Position += 64;
                    //metadata address
                    int virtualAddress = br.ReadInt32();
                    int size = br.ReadInt32();
                    if (size != 0x48)
                    {
                        Console.WriteLine("Error metadata size");
                        br.BaseStream.Position = dllBegin + 4;
                        return;
                    }
                    //for (int i = 0; i < 8; i++)
                    //{
                    //    int virtualAddress = br.ReadInt32();
                    //    int size = br.ReadInt32();
                    //    Console.WriteLine(string.Format("{0} : {1}: {2}", br.BaseStream.Length, virtualAddress + size, br.BaseStream.Length - virtualAddress - size));
                    //}

                    //section header
                    br.BaseStream.Position = peIndex + sizeOfOptionalHeader;
                    List<SectionHeader> sectionHeaders = new List<SectionHeader>();
                    for (int i = 0; i < sectionCount; i++)
                    {
                        SectionHeader header = new SectionHeader();
                        byte[] bytes = br.ReadBytes(8);
                        header.mName = Encoding.ASCII.GetString(bytes);
                        br.BaseStream.Position += 4;
                        header.mVirtualAddress = br.ReadInt32();
                        br.BaseStream.Position += 4;
                        header.mDiskAddress = br.ReadInt32();
                        br.BaseStream.Position += 16;
                        sectionHeaders.Add(header);
                    }

                    //COR20_HEADER
                    br.BaseStream.Position = dllBegin + GetDiskAddress(sectionHeaders, virtualAddress);
                    cb = br.ReadInt32();
                    if (cb != 0x48)
                    {
                        Console.WriteLine("Error COR20 header");
                        br.BaseStream.Position = dllBegin + 4;
                        return;
                    }
                    br.BaseStream.Position += 4;
                    int metadataAddress = GetDiskAddress(sectionHeaders, br.ReadInt32());
                    /*int metadateSize = */
                    br.ReadInt32();
                    //metadata header bsjb
                    br.BaseStream.Position = metadataAddress + dllBegin;
                    if (br.BaseStream.Position > br.BaseStream.Length)
                    {
                        return;
                    }
                    long metaBegin = br.BaseStream.Position;
                    br.BaseStream.Position += 12;
                    int vesionSize = br.ReadInt32();
                    long versionBegin = br.BaseStream.Position;
                    int versionStrCount;
                    br.BaseStream.Position = versionBegin + vesionSize + 2;
                    int streamNumbers = br.ReadInt16();
                    //#~ streams
                    int firstStr = 0;
                    int secondStr = 0;
                    //streams
                    for (int i = 0; i < streamNumbers; i++)
                    {
                        int off = br.ReadInt32();
                        if (i == 0)
                        {
                            firstStr = off;
                        }
                        else if (i == 1)
                        {
                            secondStr = off;
                        }
                        /*int streamSize = */
                        br.ReadInt32();
                        versionStrCount = 0;
                        while (br.ReadByte() != '\0')
                        {
                            versionStrCount++;
                        }
                        versionStrCount++;
                        if (versionStrCount % 4 != 0)
                        {
                            br.BaseStream.Position += 4 - versionStrCount % 4;
                        }
                    }

                    //first stream
                    long realAdrress = firstStr + metaBegin;
                    br.BaseStream.Position = realAdrress;
                    br.BaseStream.Position += 8;
                    long mask = br.ReadInt64();
                    int maskCount = BitCount(mask);
                    br.BaseStream.Position += 8 + maskCount * 4;
                    //module
                    br.BaseStream.Position += 2;
                    int strIndex = br.ReadInt16();
                    //Console.WriteLine(strIndex);

                    realAdrress = secondStr + metaBegin + strIndex;
                    br.BaseStream.Position = realAdrress;
                    if (br.BaseStream.Position > br.BaseStream.Length)
                    {
                        return;
                    }
                    long dllNameBegin = realAdrress;
                    versionStrCount = 0;
                    while (br.ReadByte() != '\0')
                    {
                        versionStrCount++;
                    }
                    br.BaseStream.Position = dllNameBegin;
                    byte[] nameBytes = br.ReadBytes(versionStrCount);
                    string dllName = Encoding.ASCII.GetString(nameBytes);
                    //Console.WriteLine(dllName);
                    //Console.WriteLine("{0} : {1}", total, br.BaseStream.Length);
                    br.BaseStream.Position = total;

                    SaveDll(br.BaseStream, dllBegin, total, dllName);
                    //int index = 0;
                    //long sectionIndex = beginIndex + sizeOfHeaders;
                    //while (index != sectionCount)
                    //{
                    //    br.BaseStream.Position += 16;
                    //    int sizeOfRawData = br.ReadInt32();
                    //    int a = sizeOfRawData / fileAlignment ;
                    //    int b = sizeOfRawData % fileAlignment ;
                    //    if (b > 0)
                    //    {
                    //        a++;
                    //    }
                    //    sectionIndex += a * fileAlignment ;
                    //    br.BaseStream.Position += 20;
                    //    index++;
                    //}
                }
            }
        }

        private static void SaveDll(Stream fromFile, long start, int count, string name)
        {
            name = Path.GetFileNameWithoutExtension(name);
            if (!File.Exists(mDirectorPath + "/" + name + ".dll"))
            {
                new FileStream(mDirectorPath + "/" + name + ".dll", FileMode.Create, FileAccess.Write).Close();
            }
            else
            {
                return;
                //var fs = new FileStream(mDirectorPath + "/" + name + ".dll", FileMode.Open, FileAccess.Write);
                //fs.SetLength(0);
                //fs.Flush();
                //fs.Close();
            }
            FileStream toFile =
                new FileStream(mDirectorPath + "/" + name + ".dll", FileMode.Truncate, FileAccess.Write);
            fromFile.Position = start;
            int eachReadLength = 1024;
            if (eachReadLength < count)
            {
                byte[] buffer = new byte[eachReadLength];
                long copied = 0;
                while (copied <= count - eachReadLength)
                {
                    fromFile.Read(buffer, 0, eachReadLength);
                    toFile.Write(buffer, 0, eachReadLength);
                    toFile.Flush();
                    copied += eachReadLength;
                }
                int left = (int)(count - copied);
                fromFile.Read(buffer, 0, left);
                toFile.Write(buffer, 0, left);
                toFile.Flush();
            }
            else
            {
                byte[] buffer = new byte[count];
                fromFile.Read(buffer, 0, count);
                toFile.Write(buffer, 0, count);
                toFile.Flush();
            }
            toFile.Close();
        }

        public class SectionHeader
        {
            public int mDiskAddress;
            public string mName;
            public int mVirtualAddress;
        }
    }
}