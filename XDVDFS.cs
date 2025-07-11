using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace XboxKit
{
    internal class XDVDFS
    {
        public const long XISO_HEADER_OFFSET = 0x10000;
        public static readonly byte[] FILLER = Encoding.ASCII.GetBytes("ABCDABCDABCDABCD");
        public static readonly byte[] MAGIC = Encoding.ASCII.GetBytes("XBOX_DVD_LAYOUT_TOOL_SIG");

        // Traverse file tree to get all valid data sectors in XISO
        public static void GetValidSectors(FileStream isoFS, long isoOffset, List<uint> validSectors, long rootOffset, uint rootSize, long childOffset)
        {
            if (childOffset >= rootSize)
                return;

            long cur = isoOffset + rootOffset + childOffset;
            long curOffset = cur / Utils.SECTOR_SIZE;
            long curSize = (rootSize - childOffset + Utils.SECTOR_SIZE - 1) / Utils.SECTOR_SIZE;
            for (long i = curOffset; i < curOffset + curSize; i++)
                validSectors.Add((uint)i);

            isoFS.Seek(cur, SeekOrigin.Begin);

            ushort leftChildOffset = Utils.ReadUShort(isoFS);
            ushort rightChildOffset = Utils.ReadUShort(isoFS);
            long entryOffset = (long)Utils.ReadUInt(isoFS) * Utils.SECTOR_SIZE;
            uint entrySize = Utils.ReadUInt(isoFS);
            bool isDirectory = ((byte)isoFS.ReadByte() & 0x10) != 0;
 
            if (leftChildOffset == 0xFFFF)
                return;

            if (leftChildOffset != 0)
                GetValidSectors(isoFS, isoOffset, validSectors, rootOffset, rootSize, (long)leftChildOffset * 4);

            if (isDirectory)
                GetValidSectors(isoFS, isoOffset, validSectors, entryOffset, entrySize, 0);
            else
            {
                long fileOffset = (isoOffset + entryOffset) / Utils.SECTOR_SIZE;
                long fileSize = (entrySize + Utils.SECTOR_SIZE - 1) / Utils.SECTOR_SIZE;
                for (long i = fileOffset; i < fileOffset + fileSize; i++)
                    validSectors.Add((uint)i);
            }

            if (rightChildOffset != 0)
                GetValidSectors(isoFS, isoOffset, validSectors, rootOffset, rootSize, (long)rightChildOffset * 4);
        }

        // Get list of valid XISO ranges
        public static List<(uint, uint)> GetXISORanges(FileStream isoFS, long offset)
        {
            List<uint> validSectors = new List<uint>();
            long headerOffset = offset + XDVDFS.XISO_HEADER_OFFSET;
            long headerOffsetSector = (headerOffset) / Utils.SECTOR_SIZE;
            validSectors.Add((uint)headerOffsetSector);
            validSectors.Add((uint)headerOffsetSector + 1);

            isoFS.Seek(headerOffset + 20, SeekOrigin.Begin);
            uint rootOffset = Utils.ReadUInt(isoFS);
            uint rootSize = Utils.ReadUInt(isoFS);
            GetValidSectors(isoFS, offset, validSectors, (long)rootOffset * Utils.SECTOR_SIZE, rootSize, 0);

            var ranges = new List<(uint, uint)>();
            var sortedSectors = validSectors.Distinct().OrderBy(x => x).ToList();
            uint start = sortedSectors[0];
            uint prev = sortedSectors[0];
            for (int i = 1; i < sortedSectors.Count; i++)
            {
                uint current = sortedSectors[i];
                if (current == prev + 1)
                    prev = current;
                else
                {
                    ranges.Add((start, prev));
                    start = current;
                    prev = current;
                }
            }
            ranges.Add((start, prev));

            return ranges;
        }

        public static long SUOffset(FileStream videoFS)
        {
            long updateOffset = videoFS.Length;
            byte[] videoBuf = new byte[16];
            while (updateOffset > 0)
            {
                videoFS.Seek(updateOffset - Utils.SECTOR_SIZE, SeekOrigin.Begin);
                videoFS.Read(videoBuf, 0, 16);
                if (FILLER.AsSpan().SequenceEqual(videoBuf))
                    break;

                updateOffset -= Utils.SECTOR_SIZE;
            }
            return updateOffset;
        }
    }
}