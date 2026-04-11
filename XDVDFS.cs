using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace XboxKit
{
    internal class XDVDFS
    {
        public const long SECTOR_SIZE = 2048;
        public const long XISO_HEADER_OFFSET = 0x10000;
        public static readonly byte[] FILLER = Encoding.ASCII.GetBytes("ABCDABCDABCDABCD");
        public static readonly byte[] MAGIC1 = Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA");
        public static readonly byte[] MAGIC2 = Encoding.ASCII.GetBytes("XBOX_DVD_LAYOUT_TOOL_SIG");

        // Traverse file tree to get all valid data sectors in XISO
        public static void GetValidSectors(FileStream isoFS, long isoOffset, List<uint> sysSectors, List<uint> fileSectors, long rootOffset, uint rootSize, long childOffset, bool quiet)
        {
            if (childOffset >= rootSize)
                return;

            long cur = isoOffset + rootOffset + childOffset;
            long curOffset = cur / SECTOR_SIZE;
            long curSize = (rootSize - childOffset + SECTOR_SIZE - 1) / SECTOR_SIZE;
            for (long i = curOffset; i < curOffset + curSize; i++)
                sysSectors.Add((uint)i);

            isoFS.Seek(cur, SeekOrigin.Begin);

            ushort leftChildOffset = Utils.ReadUShort(isoFS);
            if (leftChildOffset == 0xFFFF)
                return;
            ushort rightChildOffset = Utils.ReadUShort(isoFS);
            long entryOffset = (long)Utils.ReadUInt(isoFS) * SECTOR_SIZE;
            uint entrySize = Utils.ReadUInt(isoFS);
            bool isDirectory = ((byte)isoFS.ReadByte() & 0x10) != 0;

            if (leftChildOffset != 0)
                GetValidSectors(isoFS, isoOffset, sysSectors, fileSectors, rootOffset, rootSize, (long)leftChildOffset * 4, quiet);

            if (isDirectory)
                GetValidSectors(isoFS, isoOffset, sysSectors, fileSectors, entryOffset, entrySize, 0, quiet);
            else
            {
                long fileOffset = (isoOffset + entryOffset) / SECTOR_SIZE;
                long fileSize = (entrySize + SECTOR_SIZE - 1) / SECTOR_SIZE;
                for (long i = fileOffset; i < fileOffset + fileSize; i++)
                    fileSectors.Add((uint)i);
            }

            if (rightChildOffset != 0)
                GetValidSectors(isoFS, isoOffset, sysSectors, fileSectors, rootOffset, rootSize, (long)rightChildOffset * 4, quiet);
        }

        // Get list of valid XISO ranges
        public static (List<(uint, uint)> All, List<(uint, uint)> Sys, List<(uint, uint)> Files) GetXISORanges(FileStream isoFS, long offset, bool quiet)
        {
            List<uint> sysSectors = new List<uint>();
            List<uint> fileSectors = new List<uint>();
            long headerOffset = offset + XDVDFS.XISO_HEADER_OFFSET;
            long headerOffsetSector = (headerOffset) / SECTOR_SIZE;
            sysSectors.Add((uint)headerOffsetSector);
            // TODO: Don't add 2nd header sector if MAGIC is not present
            sysSectors.Add((uint)headerOffsetSector + 1);

            isoFS.Seek(headerOffset + 20, SeekOrigin.Begin);
            uint rootOffset = Utils.ReadUInt(isoFS);
            uint rootSize = Utils.ReadUInt(isoFS);
            GetValidSectors(isoFS, offset, sysSectors, fileSectors, (long)rootOffset * SECTOR_SIZE, rootSize, 0, quiet);

            var allRanges = new List<(uint, uint)>();
            var sortedAllSectors = fileSectors.Union(sysSectors).Distinct().OrderBy(x => x).ToList();
            uint start = sortedAllSectors[0];
            uint prev = sortedAllSectors[0];
            for (int i = 1; i < sortedAllSectors.Count; i++)
            {
                uint current = sortedAllSectors[i];
                if (current == prev + 1)
                    prev = current;
                else
                {
                    allRanges.Add((start, prev));
                    start = current;
                    prev = current;
                }
            }
            allRanges.Add((start, prev));

            var sysRanges = new List<(uint, uint)>();
            var sortedSysSectors = sysSectors.Distinct().OrderBy(x => x).ToList();
            start = sortedSysSectors[0];
            prev = sortedSysSectors[0];
            for (int i = 1; i < sortedSysSectors.Count; i++)
            {
                uint current = sortedSysSectors[i];
                if (current == prev + 1)
                    prev = current;
                else
                {
                    sysRanges.Add((start, prev));
                    start = current;
                    prev = current;
                }
            }
            sysRanges.Add((start, prev));

            var fileRanges = new List<(uint, uint)>();
            var sortedFileSectors = fileSectors.Distinct().OrderBy(x => x).ToList();
            start = sortedFileSectors[0];
            prev = sortedFileSectors[0];
            for (int i = 1; i < sortedFileSectors.Count; i++)
            {
                uint current = sortedFileSectors[i];
                if (current == prev + 1)
                    prev = current;
                else
                {
                    fileRanges.Add((start, prev));
                    start = current;
                    prev = current;
                }
            }
            fileRanges.Add((start, prev));

            return (allRanges, sysRanges, fileRanges);
        }

        // Heuristic to determine XGD3 system update file offset in video partition 
        // TODO: Parse ISO filesystem instead
        public static long SUOffset(FileStream videoFS)
        {
            long updateOffset = videoFS.Length;
            byte[] videoBuf = new byte[16];
            while (updateOffset >= SECTOR_SIZE)
            {
                videoFS.Seek(updateOffset - SECTOR_SIZE, SeekOrigin.Begin);
                int bytesRead = 0;
                while (bytesRead < videoBuf.Length)
                {
                    int n = videoFS.Read(videoBuf, bytesRead, videoBuf.Length - bytesRead);
                    if (n == 0)
                        break;
                    bytesRead += n;
                }
                if (FILLER.AsSpan().SequenceEqual(videoBuf))
                    break;

                updateOffset -= SECTOR_SIZE;
            }
            return updateOffset;
        }
    }
}