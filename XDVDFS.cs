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
        public static void GetValidSectors(FileStream isoFS, long isoOffset, List<uint> validSectors, long rootOffset, uint rootSize, long childOffset, bool quiet)
        {
            if (childOffset >= rootSize)
                return;

            long cur = isoOffset + rootOffset + childOffset;
            long curOffset = cur / SECTOR_SIZE;
            long curSize = (rootSize - childOffset + SECTOR_SIZE - 1) / SECTOR_SIZE;
            for (long i = curOffset; i < curOffset + curSize; i++)
                validSectors.Add((uint)i);

            isoFS.Seek(cur, SeekOrigin.Begin);

            ushort leftChildOffset = Utils.ReadUShort(isoFS);
            if (leftChildOffset == 0xFFFF)
                return;
            ushort rightChildOffset = Utils.ReadUShort(isoFS);
            long entryOffset = (long)Utils.ReadUInt(isoFS) * SECTOR_SIZE;
            uint entrySize = Utils.ReadUInt(isoFS);
            bool isDirectory = ((byte)isoFS.ReadByte() & 0x10) != 0;

            if (leftChildOffset != 0)
                GetValidSectors(isoFS, isoOffset, validSectors, rootOffset, rootSize, (long)leftChildOffset * 4, quiet);

            if (isDirectory)
                GetValidSectors(isoFS, isoOffset, validSectors, entryOffset, entrySize, 0, quiet);
            else
            {
                long fileOffset = (isoOffset + entryOffset) / SECTOR_SIZE;
                long fileSize = (entrySize + SECTOR_SIZE - 1) / SECTOR_SIZE;
                for (long i = fileOffset; i < fileOffset + fileSize; i++)
                    validSectors.Add((uint)i);
            }

            if (rightChildOffset != 0)
                GetValidSectors(isoFS, isoOffset, validSectors, rootOffset, rootSize, (long)rightChildOffset * 4, quiet);
        }

        // Get list of valid XISO ranges
        public static List<(uint, uint)> GetXISORanges(FileStream isoFS, long offset, bool quiet)
        {
            List<uint> validSectors = new List<uint>();
            long headerOffset = offset + XDVDFS.XISO_HEADER_OFFSET;
            long headerOffsetSector = (headerOffset) / SECTOR_SIZE;
            validSectors.Add((uint)headerOffsetSector);
            // TODO: Don't add 2nd header sector if MAGIC is not present
            validSectors.Add((uint)headerOffsetSector + 1);

            isoFS.Seek(headerOffset + 20, SeekOrigin.Begin);
            uint rootOffset = Utils.ReadUInt(isoFS);
            uint rootSize = Utils.ReadUInt(isoFS);
            GetValidSectors(isoFS, offset, validSectors, (long)rootOffset * SECTOR_SIZE, rootSize, 0, quiet);

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

        // Heuristic to determine XGD3 system update file offset in video partition 
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