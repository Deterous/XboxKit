using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LibXGD
{
    public class XDVDFS
    {
        public const long SECTOR_SIZE = 2048;
        public const long XISO_HEADER_OFFSET = 0x10000;
        public static readonly byte[] FILLER = Encoding.ASCII.GetBytes("ABCDABCDABCDABCD");
        public static readonly byte[] MAGIC1 = Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA");
        public static readonly byte[] MAGIC2 = Encoding.ASCII.GetBytes("XBOX_DVD_LAYOUT_TOOL_SIG");

        // Validate XISO by checking for XDVDFS magic at volume descriptor
        public static bool IsValidXISO(FileStream isoFS, long offset = 0)
        {
            long headerOffset = offset + XISO_HEADER_OFFSET;
            if (isoFS.Length < headerOffset + MAGIC1.Length)
                return false;
            isoFS.Seek(headerOffset, SeekOrigin.Begin);
            byte[] magic = new byte[MAGIC1.Length];
            if (isoFS.Read(magic, 0, magic.Length) != magic.Length)
                return false;
            isoFS.Seek(0, SeekOrigin.Begin);
            return magic.SequenceEqual(MAGIC1);
        }

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
            long entryOffset = Utils.ReadUInt(isoFS) * SECTOR_SIZE;
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
        public static (List<(uint Start, uint End)> All, List<(uint Start, uint End)> Sys, List<(uint Start, uint End)> Files) GetXISORanges(FileStream isoFS, long offset, bool quiet)
        {
            List<uint> sysSectors = new List<uint>();
            List<uint> fileSectors = new List<uint>();
            long headerOffset = offset + XDVDFS.XISO_HEADER_OFFSET;
            long headerOffsetSector = headerOffset / SECTOR_SIZE;
            sysSectors.Add((uint)headerOffsetSector);

            isoFS.Seek(headerOffset + 20, SeekOrigin.Begin);
            uint rootOffset = Utils.ReadUInt(isoFS);
            uint rootSize = Utils.ReadUInt(isoFS);

            isoFS.Seek(headerOffset + SECTOR_SIZE, SeekOrigin.Begin);
            byte[] magic = new byte[24];
            if (isoFS.Read(magic, 0, 24) != 24)
                throw new EndOfStreamException("[ERROR] Failed to read XISO ranges");
            if (magic.SequenceEqual(MAGIC2))
                sysSectors.Add((uint)headerOffsetSector + 1);

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
            if (sortedFileSectors.Count > 0)
            {
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
            }

            return (allRanges, sysRanges, fileRanges);
        }

        // Process XISO: extract filler, wipe, trim, and/or create skeleton
        public static bool ProcessXISO(FileStream isoFS, long isoOffset, long xisoLength, FileStream? xisoFS, FileStream? fillerFS, bool wipe, bool trim, bool skeleton, bool quiet)
        {
            if (xisoFS == null && fillerFS == null)
                return true;

            // Parse XISO filesystem for all file extents
            var (ranges, bones, _) = GetXISORanges(isoFS, isoOffset, quiet);
            if (!quiet) foreach (var (start, end) in ranges) Console.WriteLine($"[INFO] XISO File Extent: {start}-{end}");

            bool writeXISO = xisoFS != null;
            bool extractFiller = fillerFS != null;

            isoFS.Seek(isoOffset, SeekOrigin.Begin);
            long numBytes = 0;
            while (numBytes < xisoLength)
            {
                long currentByte = isoOffset + numBytes;
                long currentSector = (currentByte + SECTOR_SIZE - 1) / SECTOR_SIZE;
                long bytesUntilEndOfExtent = 0;
                long bytesToWipe = 0;
                bool skipEnd = false;

                // Determine whether current sector is after last file extent
                if (ranges.Count > 0 && currentSector > ranges[ranges.Count - 1].End)
                {
                    long bytesUntilEnd = xisoLength - numBytes;
                    if (extractFiller || wipe)
                        bytesToWipe = bytesUntilEnd;

                    if (trim)
                    {
                        skipEnd = true;
                        if (!quiet) Console.WriteLine($"[INFO] Trimming XISO");
                    }
                    if (trim && !extractFiller)
                    {
                        numBytes += bytesUntilEnd;
                        break;
                    }
                }
                else if (extractFiller || writeXISO)
                {
                    for (int i = 0; i < ranges.Count; i++)
                    {
                        if (currentSector >= ranges[i].Start && currentSector <= ranges[i].End)
                        {
                            bytesUntilEndOfExtent = (ranges[i].End + 1) * SECTOR_SIZE - currentByte;
                            break;
                        }
                        else if (currentSector < ranges[i].Start && (i == 0 || currentSector > ranges[i - 1].End))
                        {
                            bytesToWipe = ranges[i].Start * SECTOR_SIZE - currentByte;
                            break;
                        }
                    }
                }

                // Write filler data to file
                if (extractFiller)
                {
                    if (bytesToWipe > 0)
                    {
                        if (!Utils.WriteBytes(isoFS, fillerFS!, -1, bytesToWipe))
                            return false;
                        if (!writeXISO)
                            numBytes += bytesToWipe;
                    }
                    else if (!writeXISO)
                    {
                        long bytesToEnd = bytesUntilEndOfExtent > 0 ? bytesUntilEndOfExtent : xisoLength - numBytes;
                        isoFS.Seek(bytesToEnd, SeekOrigin.Current);
                        numBytes += bytesToEnd;
                    }
                }

                // Write to XISO file
                if (writeXISO)
                {
                    bool fillerAlreadyRead = extractFiller && bytesToWipe > 0;

                    if (wipe && bytesToWipe > 0 && !skipEnd)
                    {
                        // Write zeroes to XISO
                        if (bytesToWipe % SECTOR_SIZE != 0)
                            return false;
                        Utils.WriteZeroes(xisoFS!, -1, bytesToWipe);
                        numBytes += bytesToWipe;
                        if (!fillerAlreadyRead)
                            isoFS.Seek(bytesToWipe, SeekOrigin.Current);
                    }
                    else if (!skipEnd)
                    {
                        long bytesToRead;
                        if (bytesToWipe > 0)
                            bytesToRead = bytesToWipe;
                        else if (bytesUntilEndOfExtent > 0)
                            bytesToRead = bytesUntilEndOfExtent;
                        else
                            bytesToRead = xisoLength - numBytes;

                        // Check if current sector is a filesystem sector
                        bool is_bone = false;
                        for (int i = 0; i < bones.Count; i++)
                        {
                            if (currentSector >= bones[i].Start && currentSector <= bones[i].End)
                            {
                                is_bone = true;
                                bytesToRead = (bones[i].End + 1) * SECTOR_SIZE - currentByte;
                                break;
                            }
                        }

                        if (fillerAlreadyRead)
                        {
                            if (wipe || skeleton)
                            {
                                // Write zeroes over filler data area
                                Utils.WriteZeroes(xisoFS!, -1, bytesToRead);
                            }
                            else
                            {
                                // Filler already extracted, but needs to be retained in XISO too
                                isoFS.Seek(-bytesToRead, SeekOrigin.Current);
                                if (!Utils.WriteBytes(isoFS, xisoFS!, -1, bytesToRead))
                                    return false;
                            }
                        }
                        else if (skeleton && !is_bone)
                        {
                            // Skip file data in XISO
                            Utils.WriteZeroes(xisoFS!, -1, bytesToRead);
                            isoFS.Seek(bytesToRead, SeekOrigin.Current);
                        }
                        else
                        {
                            // Write file data to XISO
                            if (!Utils.WriteBytes(isoFS, xisoFS!, -1, bytesToRead))
                                return false;
                        }

                        numBytes += bytesToRead;
                    }
                    else if (bytesToWipe > 0)
                    {
                        if (!fillerAlreadyRead)
                            isoFS.Seek(bytesToWipe, SeekOrigin.Current);
                        numBytes += bytesToWipe;
                    }
                }
            }

            return numBytes == xisoLength;
        }

    }
}
