using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace XboxKit
{
    internal class Program
    {
        #region Constants

        static readonly int SECTOR_SIZE = 2048;
        static readonly long XISO_HEADER_OFFSET = 0x10000;
        static readonly byte[] FILLER = Encoding.ASCII.GetBytes("ABCDABCDABCDABCD");
        static readonly byte[] XDVDFS_MAGIC = Encoding.ASCII.GetBytes("XBOX_DVD_LAYOUT_TOOL_SIG");
        // XISO Types:                              XGD1,    XGD2,   XGD2-Hybrid,    XGD3
        static readonly long[] XISO_OFFSET = [0x18300000, 0xFD90000, 0x89D80000, 0x2080000];
        static readonly long[] XISO_LENGTH = [0x1A2DB0000, 0x1B3880000, 0xBF8A0000, 0x204510000];
        // Redump ISO Types:                               XGD1,      XGD2w0,      XGD2w1,      XGD2w2,     XGD2w3+, XGD2-Hybrid,      XGD3v0,     XGD3
        static readonly long[] REDUMP_ISO_LENGTH = [0x1D26A8000, 0x1D3301800, 0x1D2FEF800, 0x1D3082000, 0x1D3390000, 0x1D31A0000, 0x208E05800, 0x208E03800];
        // Video Partition Types:                   XGD1,  XGD2w0,   XGD2w1,  XGD2w2,   XGD2w3,    XGD2w4-7,   XGD2w8-9, XGD2w10-12,  XGD2w13, XGD2w14-15, XGD2w16,  XGD2w17-18, XGD2w19,  XGD2w20,  XGD2-Hybrid, XGD3v0,    XGD3
        static readonly long[] VIDEO_L0_LENGTH = [0xD58000, 0xA8000, 0x548000, 0x438000, 0x4BB0000, 0x56C0000, 0x5460000, 0x5BA0000, 0x5C10000, 0x55D0000, 0x55C0000, 0x8A40000, 0x8A90000, 0x8E80000, 0x4B1D0000, 0x1880000, 0x1880000];
        static readonly long[] VIDEO_L1_LENGTH = [0x50000, 0x9800, 0x197800, 0x11A000, 0x4BA0000, 0x56B0000, 0x5450000, 0x5B90000, 0x5C00000, 0x55C0000, 0x55B0000, 0x8A30000, 0x8A80000, 0x8E70000, 0x4AFD0000, 0x1875800, 0x1873800];
        static readonly long[] VIDEO_LENGTH = new long[VIDEO_L0_LENGTH.Length];
        // Wave Types:                            XGD2w0,             XGD2w1,             XGD2w2,             XGD2w3,             XGD2w4,             XGD2w5,             XGD2w6,             XGD2w7,             XGD2w8,             XGD2w9,            XGD2w10,            XGD2w11,            XGD2w12,            XGD2w13,            XGD2w14,            XGD2w15,            XGD2w16,            XGD2w17,            XGD2w18,            XGD2w19,            XGD2w20,           XGD2-Hybrid,           XGD1
        static readonly string[] WAVE_PVD = ["2004083110334900", "2005100712184600", "2006030621090700", "2009011416000000", "2009082417000000", "2009100517000000", "2009102917000000", "2010022116000000", "2010090417000000", "2010091517000000", "2010102817000000", "2011011816000000", "2011061217000000", "2011071217000000", "2011120716000000", "2012022116000000", "2012062117000000", "2012110716000000", "2012111816000000", "2013082617000000", "2015042617000000", "2006041012132800", "2001091310425500"];

        #endregion

        #region Helper functions

        // Print help for invalid command
        static void PrintHelp()
        {
            Console.WriteLine("XboxKit (c) Deterous 2024-2025");
            Console.WriteLine("");
            Console.WriteLine("Usage: xboxkit.exe [options] <input.iso> [video.iso] [filler_data] [system_update_file]");
            Console.WriteLine("");
            Console.WriteLine("Rebuild mode: Combine input files (no options)");
            Console.WriteLine("Extract mode: Use options (other paths are used for custom output file names)");
            Console.WriteLine("-a, --all   \t Perform all operations on the input ISO");
            Console.WriteLine("-q, --quiet \t Don't print INFO messages to console");
            Console.WriteLine("-r, --random\t Extracts random filler data to a separate file");
            Console.WriteLine("-s, --seed  \t Extracts RNG seed used for XGD1 filler");
            Console.WriteLine("-t, --trim  \t Trims end of XISO (game partition)");
            Console.WriteLine("-u, --update\t Extracts update file from video ISO (XGD3 only)");
            Console.WriteLine("-v, --video \t Extracts video ISO (video partition)");
            Console.WriteLine("-w, --wipe  \t Wipes filler data in XISO");
            Console.WriteLine("-x, --xiso  \t Extracts XISO (game partition)");
        }

        // Check two byte arrays are equal
        static bool SequenceEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null)
                return false;
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }

        // Read uint16 from filestream
        static ushort ReadUShort(FileStream fs)
        {
            byte[] buffer = new byte[2];
            if (fs.Read(buffer, 0, 2) != 2)
                throw new EndOfStreamException("[ERROR] Failed to read UShort");
            return BitConverter.ToUInt16(buffer, 0);
        }

        // Read uint32 from filestream
        static uint ReadUInt(FileStream fs)
        {
            byte[] buffer = new byte[4];
            if (fs.Read(buffer, 0, 4) != 4)
                throw new EndOfStreamException("[ERROR] Failed to read UInt32");
            return BitConverter.ToUInt32(buffer, 0);
        }

        // Traverse file tree to get all valid data sectors in XISO
        static void GetValidSectors(FileStream isoFS, long isoOffset, List<uint> validSectors, long rootOffset, uint rootSize, long childOffset)
        {
            if (childOffset >= rootSize)
                return;

            long cur = isoOffset + rootOffset + childOffset;
            long curOffset = cur / SECTOR_SIZE;
            long curSize = (rootSize - childOffset + SECTOR_SIZE - 1) / SECTOR_SIZE;
            for (long i = curOffset; i < curOffset + curSize; i++)
                validSectors.Add((uint)i);

            isoFS.Seek(cur, SeekOrigin.Begin);

            ushort leftChildOffset = ReadUShort(isoFS);
            ushort rightChildOffset = ReadUShort(isoFS);
            long entryOffset = (long)ReadUInt(isoFS) * SECTOR_SIZE;
            uint entrySize = ReadUInt(isoFS);
            bool isDirectory = ((byte)isoFS.ReadByte() & 0x10) != 0;
 
            if (leftChildOffset == 0xFFFF)
                return;

            if (leftChildOffset != 0)
                GetValidSectors(isoFS, isoOffset, validSectors, rootOffset, rootSize, (long)leftChildOffset * 4);

            if (isDirectory)
                GetValidSectors(isoFS, isoOffset, validSectors, entryOffset, entrySize, 0);
            else
            {
                long fileOffset = (isoOffset + entryOffset) / SECTOR_SIZE;
                long fileSize = (entrySize + SECTOR_SIZE - 1) / SECTOR_SIZE;
                for (long i = fileOffset; i < fileOffset + fileSize; i++)
                    validSectors.Add((uint)i);
            }

            if (rightChildOffset != 0)
                GetValidSectors(isoFS, isoOffset, validSectors, rootOffset, rootSize, (long)rightChildOffset * 4);
        }

        // Get list of valid XISO ranges
        static List<(uint, uint)> GetXISORanges(FileStream isoFS, long offset)
        {
            List<uint> validSectors = new List<uint>();
            long headerOffset = offset + XISO_HEADER_OFFSET;
            long headerOffsetSector = (headerOffset) / SECTOR_SIZE;
            validSectors.Add((uint)headerOffsetSector);
            validSectors.Add((uint)headerOffsetSector + 1);

            isoFS.Seek(headerOffset + 20, SeekOrigin.Begin);
            uint rootOffset = ReadUInt(isoFS);
            uint rootSize = ReadUInt(isoFS);
            GetValidSectors(isoFS, offset, validSectors, (long)rootOffset * SECTOR_SIZE, rootSize, 0);

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

        // Ensure proper writing to byte array
        static bool WriteBytes(FileStream fs, byte[] outBA, long offset)
        {
            long numBytes = 0;
            if (offset >= 0)
                fs.Seek(offset, SeekOrigin.Begin);
            while (numBytes < outBA.Length)
            {
                int bytesRead = fs.Read(outBA, 0, (int)(outBA.Length - numBytes));
                if (bytesRead == 0)
                    break;

                numBytes += bytesRead;
            }
            return numBytes == outBA.Length;
        }

        // Ensure proper writing to filestream
        static bool WriteBytes(FileStream inFS, FileStream outFS, long offset, long length)
        {
            byte[] buf = new byte[64 * SECTOR_SIZE];
            long numBytes = 0;
            if (offset >= 0)
                inFS.Seek(offset, SeekOrigin.Begin);
            while (numBytes < length)
            {
                int bytesRead = inFS.Read(buf, 0, (int)Math.Min(buf.Length, length - numBytes));
                if (bytesRead == 0)
                    break;

                outFS.Write(buf, 0, bytesRead);
                numBytes += bytesRead;
            }
            return numBytes == length;
        }

        // Write zeroes to filestream
        static void WriteZeroes(FileStream outFS, long offset, long length)
        {
            byte[] buf = new byte[64 * SECTOR_SIZE];
            long numBytes = 0;
            if (offset >= 0)
                outFS.Seek(offset, SeekOrigin.Begin);
            while (numBytes < length)
            {
                int bytesToWrite = (int)Math.Min(buf.Length, length - numBytes);
                outFS.Write(buf, 0, bytesToWrite);
                numBytes += bytesToWrite;
            }
            return;
        }

        #endregion

        static void Main(string[] args)
        {
            #region Initial Setup

            // Initialize VIDEO_LENGTH array
            for (int i = 0; i < VIDEO_LENGTH.Length; i++)
                VIDEO_LENGTH[i] = VIDEO_L0_LENGTH[i] + VIDEO_L1_LENGTH[i];

            bool help = false;
            bool quiet = false;
            bool extractXISO = false;
            bool extractVideo = false;
            bool extractFiller = false;
            bool extractFillerIfNoSeed = false;
            bool extractSeed = false;
            bool trimXISO = false;
            bool wipeXISO = false;
            bool extractUpdate = false;
            string isoPath = string.Empty;
            string videoPath = string.Empty;
            string fillerPath = string.Empty;
            string seedPath = string.Empty;
            string updatePath = string.Empty;
            List<string> filePaths = new();

            // Check arguments
            if (args.Length == 0)
            {
                PrintHelp();
                return;
            }
            foreach (var arg in args)
            {
                if (arg.StartsWith("--"))
                {
                    switch (arg.ToLowerInvariant())
                    {
                        case "--help":
                            help = true;
                            break;
                        case "--quiet":
                            quiet = true;
                            break;
                        case "--all":
                            extractFillerIfNoSeed = true;
                            extractFiller = true;
                            extractSeed = true;
                            trimXISO = true;
                            extractUpdate = true;
                            extractVideo = true;
                            wipeXISO = true;
                            extractXISO = true;
                            break;
                        case "--random":
                            extractFiller = true;
                            break;
                        case "--seed":
                            extractSeed = true;
                            break;
                        case "--trim":
                            trimXISO = true;
                            break;
                        case "--update":
                            extractUpdate = true;
                            break;
                        case "--video":
                            extractVideo = true;
                            break;
                        case "--wipe":
                            wipeXISO = true;
                            break;
                        case "--xiso":
                            extractXISO = true;
                            break;
                        default:
                            filePaths.Add(arg);
                            break;
                    }
                }
                else if (arg.StartsWith("-") && !arg.StartsWith("--"))
                {
                    foreach (char flag in arg.Substring(1).ToLowerInvariant())
                    {
                        switch (flag)
                        {
                            case 'h':
                                help = true;
                                break;
                            case 'q':
                                quiet = true;
                                break;
                            case 'a':
                                extractFillerIfNoSeed = true;
                                extractFiller = true;
                                extractSeed = true;
                                trimXISO = true;
                                extractUpdate = true;
                                extractVideo = true;
                                wipeXISO = true;
                                extractXISO = true;
                                break;
                            case 'r':
                                extractFiller = true;
                                break;
                            case 's':
                                extractSeed = true;
                                break;
                            case 't':
                                trimXISO = true;
                                break;
                            case 'u':
                                extractUpdate = true;
                                break;
                            case 'v':
                                extractVideo = true;
                                break;
                            case 'w':
                                wipeXISO = true;
                                break;
                            case 'x':
                                extractXISO = true;
                                break;
                            default:
                                Console.WriteLine($"[ERROR] Unknown flag: -{flag}");
                                PrintHelp();
                                return;
                        }
                    }
                }
                else
                {
                    filePaths.Add(arg);
                }
            }
            if (help)
            {
                PrintHelp();
                return;
            }
            if (filePaths.Count > 0)
                isoPath = filePaths[0];
            if (filePaths.Count > 1)
                videoPath = filePaths[1];
            if (filePaths.Count > 2)
                fillerPath = filePaths[2];
            if (filePaths.Count > 3)
                updatePath = filePaths[3];

            // Determine input filenames
            string dir = Path.GetDirectoryName(isoPath);
            string filename = Path.GetFileNameWithoutExtension(isoPath);
            string extension = Path.GetExtension(isoPath);
            if (string.IsNullOrEmpty(isoPath) || !File.Exists(isoPath))
            {
                Console.WriteLine($"[ERROR] Invalid file path: {isoPath}");
                return;
            }

            // Determine output filenames
            if (string.IsNullOrEmpty(videoPath))
                videoPath = Path.Combine(dir, $"{filename}.video.iso");
            if (string.IsNullOrEmpty(fillerPath))
                fillerPath = Path.Combine(dir, $"{filename}.filler");
            if (string.IsNullOrEmpty(seedPath))
                seedPath = Path.Combine(dir, $"{filename}.seed");
            if (string.IsNullOrEmpty(updatePath))
                updatePath = Path.Combine(dir, "su20076000_00000000");
            string xisoPath = Path.Combine(dir, $"{filename}.xiso");
            string redumpPath = Path.Combine(dir, $"{filename}.redump.iso");

            // Compare input ISO file size to determine file type
            FileInfo isoInfo = new(isoPath);
            long isoSize = isoInfo.Length;
            int redumpIsoType = Array.IndexOf(REDUMP_ISO_LENGTH, isoSize);
            int videoIsoType = Array.IndexOf(VIDEO_LENGTH, isoSize);
            int xisoType = Array.IndexOf(XISO_LENGTH, isoSize);

            #endregion

            if (redumpIsoType >= 0)
            {
                #region Mode 1: Redump ISO as input

                if (wipeXISO && !extractXISO && !quiet)
                    Console.WriteLine("[INFO] Wiping XISO option (-w) does nothing without extracting XISO (-x)");
                if (trimXISO && !extractXISO && !quiet)
                    Console.WriteLine("[INFO] Trimming XISO option (-t) does nothing without extracting XISO (-x)");

                if (extractXISO && extractFiller && !wipeXISO)
                {
                    Console.WriteLine("[ERROR] Cannot write filler data without wiping XISO");
                    Console.WriteLine("        For now, use -w with -s");
                    return;
                }

                // Check that XISO doesn't already exist
                if (extractXISO && File.Exists(xisoPath))
                {
                    Console.WriteLine($"[ERROR] File already exists: {xisoPath}");
                    return;
                }

                // Check that video ISO doesn't already exist
                if (extractVideo && File.Exists(videoPath))
                {
                    Console.WriteLine($"[ERROR] File already exists: {videoPath}");
                    return;
                }

                // Check that filler data file doesn't already exist
                if (extractFiller && File.Exists(fillerPath))
                {
                    Console.WriteLine($"[ERROR] File already exists: {fillerPath}");
                    return;
                }

                // Check that update file doesn't already exist
                if (extractUpdate && File.Exists(updatePath))
                {
                    Console.WriteLine($"[ERROR] File already exists: {updatePath}");
                    return;
                }

                // Check that seed file doesn't already exist
                if (extractSeed && File.Exists(seedPath))
                {
                    Console.WriteLine($"[ERROR] File already exists: {seedPath}");
                    return;
                }

                // Determine disc layout type
                long xgdType = redumpIsoType switch
                {
                    0 => 0, // XGD1
                    1 or 2 or 3 or 4 => 1, // XGD2
                    5 => 2, // XGD2 (Hybrid)
                    6 or 7 => 3, // XGD3
                    _ => 0,
                };

                // Open redump ISO for reading
                using FileStream isoFS = new(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!quiet)
                    Console.WriteLine($"[INFO] Reading redump ISO from {isoPath}");

                // Extract video partition
                if (extractVideo)
                {
                    // Compare PVD creation datetime against known datetimes to determine wave
                    int? wave = null;
                    if (redumpIsoType == 4)
                    {
                        try
                        {
                            isoFS.Seek(0x832D, SeekOrigin.Begin);
                            byte[] pvd = new byte[16];
                            int bytesRead = isoFS.Read(pvd, 0, pvd.Length);
                            if (bytesRead == 16)
                            {
                                wave = Array.IndexOf(WAVE_PVD, Encoding.ASCII.GetString(pvd));
                            }
                            else
                            {
                                Console.WriteLine($"[ERROR] Failed to read PVD from {isoPath}");
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ERROR] {ex.Message}");
                            return;
                        }
                    }

                    // Determine size of output video ISO
                    int videoType = redumpIsoType switch
                    {
                        0 => 0, // XGD1
                        1 => 1, // XGD2 Wave 0
                        2 => 2, // XGD2 Wave 1
                        3 => 3, // XGD2 Wave 2
                        4 => wave switch // XGD2 Wave 3-20
                        {
                            0 => 1,                // E9B8ECFE
                            1 => 2,                // 739CEAB3
                            2 => 3,                // A4CFB59C
                            3 => 4,                // 2A4CCBD3
                            4 or 5 or 6 or 7 => 5, // 05C6C409
                            8 or 9 => 6,           // 0441D6A5
                            10 or 11 or 12 => 7,   // E18BC70B
                            13 => 8,               // 40DCB18F
                            14 or 15 => 9,         // 23A198FC
                            16 => 10,              // AB25DB47
                            17 or 18 => 11,        // 169EF597
                            19 => 12,              // 169EF597
                            20 => 13,              // 032CCF37
                            21 => 14,              // F48D24B8
                            22 => 0,               // 8FC52135
                            _ => -1,
                        },
                        5 => 14, // XGD2 (Hybrid)
                        6 => 15, // XGD3 (v0)
                        7 => 16, // XGD3
                        _ => -1,
                    };
                    if (videoType == -1)
                    {
                        Console.WriteLine("[ERROR] Unexpected video partition. Cannot determine wave");
                        return;
                    }

                    // Create file for video partition
                    using FileStream videoFS = new(videoPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    if (!quiet)
                        Console.WriteLine($"[INFO] Writing video partition to {videoPath}");

                    // Write layer 0 portion of video partition
                    long l0Length = VIDEO_L0_LENGTH[videoType];
                    if (!WriteBytes(isoFS, videoFS, 0, l0Length))
                    {
                        Console.WriteLine("[ERROR] Failed reading video partition.");
                        return;
                    }

                    // Write layer 1 portion of video partition
                    long l1Length = VIDEO_L1_LENGTH[videoType];
                    if (!WriteBytes(isoFS, videoFS, isoSize - l1Length, l1Length))
                    {
                        Console.WriteLine("[ERROR] Failed reading video partition.");
                        return;
                    }
                }

                // Extract system update file from XGD3 video partition
                if (extractUpdate && xgdType == 3)
                {
                    // Open video ISO for reading and writing
                    using FileStream videoFS = new(videoPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    long videoLength = videoFS.Length;

                    // Determine update file offset within video ISO
                    long updateOffset = videoLength;
                    byte[] videoBuf = new byte[16];
                    while (updateOffset > 0)
                    {
                        videoFS.Seek(updateOffset - SECTOR_SIZE, SeekOrigin.Begin);
                        videoFS.Read(videoBuf, 0, 16);
                        if (FILLER.AsSpan().SequenceEqual(videoBuf))
                            break;

                        updateOffset -= SECTOR_SIZE;
                    }
                    
                    // Write update file contents to file
                    if (!quiet)
                        Console.WriteLine($"[INFO] Writing system update file to {updatePath}");
                    using FileStream updateFS = new(updatePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    long updateLength = videoLength - updateOffset - SECTOR_SIZE;
                    if (!WriteBytes(videoFS, updateFS, updateOffset, updateLength))
                    {
                        Console.WriteLine("[ERROR] Failed writing system update file.");
                        return;
                    }

                    // Zero update file within XISO
                    if (!quiet)
                        Console.WriteLine($"[INFO] Zeroing system update file in {videoPath}");
                    WriteZeroes(videoFS, updateOffset, updateLength);
                }

                // If XGD1, try brute force the filler data seed
                uint xgd1Seed;
                if (extractSeed)
                {
                    if (xgdType == 0)
                    {
                        // Validate XGD1 magic bytes
                        byte[] magic = new byte[XDVDFS_MAGIC.Length];
                        if (!WriteBytes(isoFS, magic, XISO_OFFSET[xgdType] + 0x10800))
                        {
                            Console.WriteLine("[ERROR] Failed reading XGD1 XDVDFS.");
                            return;
                        }
                        if (!SequenceEqual(magic, XDVDFS_MAGIC))
                        {
                            Console.WriteLine("[ERROR] Invalid data in XDVDFS volume descriptor.");
                            return;
                        }

                        // Determine version offset
                        byte[] nextBuf = new byte[8];
                        if (!WriteBytes(isoFS, nextBuf, XISO_OFFSET[xgdType] + 0x10820))
                        {
                            Console.WriteLine("[ERROR] Failed reading XGD1 XDVDFS volume descriptor.");
                            return;
                        }
                        int versionOffset = 0x10824;
                        if (SequenceEqual(nextBuf, new byte[8]))
                            versionOffset += 0x10;

                        // Determine XGD1 version
                        byte[] versionBuf = new byte[2];
                        if (!WriteBytes(isoFS, versionBuf, XISO_OFFSET[xgdType] + versionOffset))
                        {
                            Console.WriteLine("[ERROR] Failed to read XGD1 version.");
                            return;
                        }
                        ushort version = (ushort)(versionBuf[0] | (versionBuf[1] << 8));
                        if (version == 0)
                        {
                            Console.WriteLine("[ERROR] Invalid XGD1 version (0)");
                            return;
                        }
                        else if (!quiet)
                            Console.WriteLine($"[INFO] XGD1 Version: {version}");

                        // Determine XGD1 pseudo random number generator seed, if possible
                        byte[] firstXISOSector = new byte[SECTOR_SIZE];
                        if (!WriteBytes(isoFS, firstXISOSector, XISO_OFFSET[xgdType]))
                        {
                            Console.WriteLine("[ERROR] Failed reading first XISO sector");
                            return;
                        }
                        if (XboxPRNG.GuessSeed(firstXISOSector, out uint seed))
                        {
                            xgd1Seed = seed;
                            if (!quiet)
                                Console.WriteLine($"[INFO] Filler data seed: {seed:X8}");
                            using FileStream seedFS = new(seedPath, FileMode.Create, FileAccess.Write, FileShare.None);
                            byte[] seedBytes = BitConverter.GetBytes(seed);
                            WriteBytes(seedFS, seedBytes, -1);

                            // Don't extract random filler if --all was used and a seed was found
                            if (extractFillerIfNoSeed)
                                extractFiller = false;
                        }
                    }
                }

                // Quit early if we're not extracting data from XISO
                if (!extractXISO && !extractFiller)
                    return;

                // Parse XISO filesystem for all file extents 
                List<(uint Start, uint End)> validRanges = GetXISORanges(isoFS, XISO_OFFSET[xgdType]);
                if (!quiet)
                {
                    foreach (var (start, end) in validRanges)
                        Console.WriteLine($"[INFO] XISO File Extent: {start}-{end}");
                }

                // Create file for game partition
                FileStream xisoFS = null!;
                if (extractXISO)
                {
                    xisoFS = new FileStream(xisoPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    Console.WriteLine($"[INFO] Writing game partition to {xisoPath}");
                }

                // Create file for filler data
                FileStream fillerFS = null!;
                if (extractFiller)
                {
                    fillerFS = new FileStream(fillerPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    Console.WriteLine($"[INFO] Writing random filler data to {fillerPath}");
                }

                // Process XISO
                isoFS.Seek(XISO_OFFSET[xgdType], SeekOrigin.Begin);
                long xisoLength = XISO_LENGTH[xgdType];
                long numBytes = 0;
                while (numBytes < xisoLength)
                {
                    long currentByte = XISO_OFFSET[xgdType] + numBytes;
                    long currentSector = (currentByte + SECTOR_SIZE - 1) / SECTOR_SIZE;
                    long bytesUntilEndOfExtent = long.MaxValue;
                    long bytesToWipe = 0;
                    bool skipEnd = false;

                    // Determine whether current sector is after last file extent
                    if (validRanges.Count > 0 && currentSector > validRanges[validRanges.Count - 1].End)
                    {
                        // Remainder of XISO is filler
                        long bytesUntilEnd = xisoLength - numBytes;
                        if (extractFiller || wipeXISO)
                            bytesToWipe = bytesUntilEnd;
                        
                        // Trim XISO
                        if (trimXISO)
                        {
                            skipEnd = true;
                            if (!quiet)
                                Console.WriteLine($"[INFO] Trimming XISO");
                        }
                        if (trimXISO && !extractFiller)
                        {
                            // Nothing else to do, finish processing XISO early
                            numBytes += bytesUntilEnd;
                            break;
                        }
                    }
                    else if (extractFiller || wipeXISO || trimXISO)
                    {
                        // Determine whether current sector is within a file extent or filler data
                        for (int i = 0; i < validRanges.Count; i++)
                        {
                            if (currentSector >= validRanges[i].Start && currentSector <= validRanges[i].End)
                            {
                                // Number of bytes remaining in current file extent
                                bytesUntilEndOfExtent = (validRanges[i].End + 1) * SECTOR_SIZE - currentByte;
                                break;
                            }
                            else if (currentSector < validRanges[i].Start && (i == 0 || currentSector > validRanges[i - 1].End))
                            {
                                // Wipe until next file extent
                                bytesToWipe = validRanges[i].Start * SECTOR_SIZE - currentByte;
                                break;
                            }
                        }
                    }

                    // Write filler data to file
                    if (extractFiller)
                    {
                        if (bytesToWipe > 0)
                        {
                            if (!WriteBytes(isoFS, fillerFS, -1, bytesToWipe))
                            {
                                Console.WriteLine("[ERROR] Failed writing filler data.");
                                return;
                            }
                            if (!extractXISO)
                                numBytes += bytesToWipe;
                        }
                        else if (!extractXISO)
                        {
                            // Skip file extent
                            long bytesToEnd = Math.Min(bytesUntilEndOfExtent, xisoLength - numBytes);
                            isoFS.Seek(bytesToEnd, SeekOrigin.Current);
                            numBytes += bytesToEnd;
                        }
                    }

                    // Write to XISO file
                    if (extractXISO)
                    {
                        if (wipeXISO && bytesToWipe > 0 && !skipEnd)
                        {
                            // Write zeroes to XISO (unless trimming end)
                            WriteZeroes(xisoFS, -1, bytesToWipe);
                            numBytes += bytesToWipe;

                            // Move ahead in ISO file if filler was not read
                            if (!extractFiller)
                                isoFS.Seek(bytesToWipe, SeekOrigin.Current);
                        }
                        else if (!skipEnd)
                        {
                            // Write data to XISO
                            long bytesToRead;
                            if (bytesToWipe > 0)
                                bytesToRead = Math.Min(bytesToWipe, xisoLength - currentByte);
                            else
                                bytesToRead = Math.Min(bytesUntilEndOfExtent, xisoLength - currentByte);
                            if (!WriteBytes(isoFS, xisoFS, -1, bytesToRead))
                            {
                                Console.WriteLine("[ERROR] Failed writing game partition (XISO).");
                                return;
                            }
                            numBytes += bytesToRead;
                        }
                        else if (bytesToWipe > 0)
                        {
                            isoFS.Seek(bytesToWipe, SeekOrigin.Current);
                            numBytes += bytesToWipe;
                        }
                    }
                }

                // Close files
                if (xisoFS != null)
                    xisoFS.Dispose();
                if (fillerFS != null)
                    fillerFS.Dispose();

                // Validity check
                if (numBytes != xisoLength)
                {
                    Console.WriteLine("[ERROR] Unexpected error, please report this");
                    return;
                }

                #endregion
            }
            else if (videoIsoType >= 0)
            {
                #region Mode 2: Video ISO as input

                // Check for valid options
                if (extractVideo)
                {
                    Console.WriteLine("[ERROR] Cannot extract video (-v), input file is already video.");
                    return;
                }
                if (extractXISO)
                {
                    Console.WriteLine("[ERROR] Cannot extract XISO (-x), input file is video.");
                    return;
                }
                if (extractFiller)
                {
                    Console.WriteLine("[ERROR] Cannot extract filler (-s), input file is video.");
                    return;
                }
                if (wipeXISO)
                {
                    Console.WriteLine("[ERROR] Cannot wipe XISO (-w), input file is video.");
                    return;
                }
                if (trimXISO)
                {
                    Console.WriteLine("[ERROR] Cannot trim XISO (-t), input file is video.");
                    return;
                }
                if (!extractUpdate)
                {
                    Console.WriteLine("[ERROR] Use -u flag to extract system update from video partition.");
                    return;
                }

                // Check that update file doesn't already exist
                if (File.Exists(updatePath))
                {
                    Console.WriteLine($"[ERROR] System update file already exists: {updatePath}");
                    return;
                }

                // Check that video partition is from XGD3 disc
                if (videoIsoType != 15 && videoIsoType != 16)
                {
                    Console.WriteLine("[ERROR] Can only extract su20076000_00000000 from XGD3 video partitions.");
                    return;
                }

                // Open ISO for reading and writing
                using FileStream videoFS = new(isoPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                long updateOffset = videoFS.Length;
                byte[] videoBuf = new byte[16];
                while (updateOffset > 0)
                {
                    videoFS.Seek(updateOffset - SECTOR_SIZE, SeekOrigin.Begin);
                    videoFS.Read(videoBuf, 0, 16);
                    if (FILLER.AsSpan().SequenceEqual(videoBuf))
                        break;

                    updateOffset -= SECTOR_SIZE;
                }

                Console.WriteLine($"[INFO] Writing system update file to {updatePath}");
                using FileStream updateFS = new(updatePath, FileMode.Create, FileAccess.Write, FileShare.None);
                long updateLength = videoFS.Length - updateOffset - SECTOR_SIZE;
                if (!WriteBytes(videoFS, updateFS, updateOffset, updateLength))
                {
                    Console.WriteLine("[ERROR] Failed writing system update file.");
                    return;
                }

                // Zero out the update file in the video ISO
                WriteZeroes(videoFS, updateOffset, updateLength);

                #endregion
            }
            else
            {
                // Mode 3: XISO as input

                long xisoLength = isoSize; // TODO: Set this to intended XISO length if input is trimmed

                #region Wipe XISO

                // Check for invalid options
                bool invalidOptions = false;
                if (extractXISO)
                {
                    Console.WriteLine("[ERROR] Cannot extract XISO (-x), input file is already XISO");
                    invalidOptions = true;
                }
                if (extractVideo)
                {
                    Console.WriteLine("[ERROR] Cannot extract video (-v), input file is XISO");
                    invalidOptions = true;
                }
                if (extractUpdate)
                {
                    Console.WriteLine("[ERROR] Cannot extract update (-u), input file is XISO");
                    invalidOptions = true;
                }
                if (invalidOptions)
                    return;

                using FileStream isoFS = new(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!quiet)
                    Console.WriteLine($"[INFO] Reading XISO from {isoPath}");

                bool writeXISO = wipeXISO || trimXISO;
                if (extractFiller || writeXISO)
                {
                    // Cannot extract/wipe/trim from invalid XISO size
                    if (xisoType < 0)
                    {
                        Console.WriteLine("[ERROR] Unexpected XISO size. Your file may be trimmed or corrupt.");
                        Console.WriteLine("        Use the full XISO if you want to trim/wipe/extract filler.");
                        return;
                    }

                    // Create file for game partition
                    FileStream xisoFS = null!;
                    if (writeXISO)
                        xisoFS = new FileStream(xisoPath, FileMode.Create, FileAccess.Write, FileShare.None);

                    // Create file for filler data
                    FileStream fillerFS = null!;
                    if (extractFiller)
                        fillerFS = new FileStream(fillerPath, FileMode.Create, FileAccess.Write, FileShare.None);

                    // Parse XISO filesystem for all file extents 
                    List<(uint Start, uint End)> validRanges = GetXISORanges(isoFS, 0);
                    if (!quiet)
                    {
                        foreach (var (start, end) in validRanges)
                            Console.WriteLine($"[INFO] XISO File Extent: {start}-{end}");
                    }

                    if (extractFiller && !quiet)
                        Console.WriteLine($"[INFO] Extracting filler data to {fillerPath}");
                    if (wipeXISO && !quiet)
                        Console.WriteLine($"[INFO] Writing wiped XISO to {xisoPath}");
                    if (!wipeXISO && trimXISO && !quiet)
                        Console.WriteLine($"[INFO] Writing XISO to {xisoPath}");

                    isoFS.Seek(0, SeekOrigin.Begin);
                    long currentByte = 0;
                    while (currentByte < isoSize)
                    {
                        long currentSector = (currentByte + SECTOR_SIZE - 1) / SECTOR_SIZE;
                        long bytesUntilEndOfExtent = long.MaxValue;
                        long bytesToWipe = 0;
                        bool skipEnd = false;

                        // Determine whether current sector is after last file extent
                        if (validRanges.Count > 0 && currentSector > validRanges[validRanges.Count - 1].End)
                        {
                            // Remainder of XISO is filler
                            long bytesUntilEnd = isoSize - currentByte;
                            if (extractFiller || wipeXISO)
                                bytesToWipe = bytesUntilEnd;
                            
                            // Trim XISO
                            if (trimXISO)
                            {
                                skipEnd = true;
                                if (!quiet)
                                    Console.WriteLine($"[INFO] Trimming XISO");
                            }
                            if (trimXISO && !extractFiller)
                            {
                                // Nothing else to do, finish processing XISO early
                                currentByte += bytesUntilEnd;
                                break;
                            }
                        }
                        else if (extractFiller || writeXISO)
                        {
                            // Determine whether current sector is within a file extent or filler data
                            for (int i = 0; i < validRanges.Count; i++)
                            {
                                if (currentSector >= validRanges[i].Start && currentSector <= validRanges[i].End)
                                {
                                    // Number of bytes remaining in current file extent
                                    bytesUntilEndOfExtent = (validRanges[i].End + 1) * SECTOR_SIZE - currentByte;
                                    break;
                                }
                                else if (currentSector < validRanges[i].Start && (i == 0 || currentSector > validRanges[i - 1].End))
                                {
                                    // Wipe until next file extent
                                    bytesToWipe = validRanges[i].Start * SECTOR_SIZE - currentByte;
                                    break;
                                }
                            }
                        }

                        // Write filler data to file
                        if (extractFiller)
                        {
                            if (bytesToWipe > 0)
                            {
                                if (!WriteBytes(isoFS, fillerFS, -1, bytesToWipe))
                                {
                                    Console.WriteLine("[ERROR] Failed writing filler data.");
                                    return;
                                }
                                if (!writeXISO)
                                    currentByte += bytesToWipe;
                            }
                            else if (!writeXISO)
                            {
                                // Skip file extent
                                long bytesToEnd = Math.Min(bytesUntilEndOfExtent, isoSize - currentByte);
                                isoFS.Seek(bytesToEnd, SeekOrigin.Current);
                                currentByte += bytesToEnd;
                            }
                        }

                        // Write to XISO file
                        if (writeXISO)
                        {
                            if (wipeXISO && bytesToWipe > 0 && !skipEnd)
                            {
                                // Write zeroes to XISO (unless trimming end)
                                WriteZeroes(xisoFS, -1, bytesToWipe);
                                currentByte += bytesToWipe;

                                // Move ahead in ISO file if filler was not read
                                if (!extractFiller)
                                    isoFS.Seek(bytesToWipe, SeekOrigin.Current);
                            }
                            else if (!skipEnd)
                            {
                                // Write data to XISO
                                long bytesToRead;
                                if (bytesToWipe > 0)
                                    bytesToRead = Math.Min(bytesToWipe, isoSize - currentByte);
                                else
                                    bytesToRead = Math.Min(bytesUntilEndOfExtent, isoSize - currentByte);
                                if (!WriteBytes(isoFS, xisoFS, -1, bytesToRead))
                                {
                                    Console.WriteLine("[ERROR] Failed writing game partition (XISO).");
                                    return;
                                }
                                currentByte += bytesToRead;
                            }
                            else if (bytesToWipe > 0)
                            {
                                // Trim end of XISO
                                isoFS.Seek(bytesToWipe, SeekOrigin.Current);
                                currentByte += bytesToWipe;
                            }
                        }
                    }

                    // Close files
                    if (xisoFS != null)
                        xisoFS.Dispose();
                    if (fillerFS != null)
                        fillerFS.Dispose();

                    // Validity check
                    if (currentByte != isoSize)
                    {
                        Console.WriteLine("[ERROR] Unexpected error, please report this");
                        return;
                    }

                    return;
                }

                #endregion

                #region Rebuild Redump ISO

                // Check that video partition exists
                if (!File.Exists(videoPath))
                {
                    Console.WriteLine($"[ERROR] Invalid file path: {videoPath}");
                    Console.WriteLine("Provide a file path to the video partition to rebuild the redump ISO.");
                    return;
                }

                if (xisoType < 0 && !File.Exists(fillerPath))
                {
                    Console.WriteLine("[ERROR] Unexpected XISO size. Your file may be trimmed or corrupt.");
                    Console.WriteLine("        Cannot rebuild redump ISO from trimmed XISO without filler.");
                    return;
                }

                // Determine video type based on video partition size
                FileInfo videoInfo = new(videoPath);
                long videoSize = videoInfo.Length;
                int videoType = Array.IndexOf(VIDEO_LENGTH, videoSize);
                if (videoType < 0)
                {
                    Console.WriteLine("[ERROR] Unexpected video partition ISO size. Your video file may be trimmed or corrupt.");
                    return;
                }

                // Determine length of output redump ISO
                long redumpLength = videoType switch
                {
                    0 => REDUMP_ISO_LENGTH[0], // XGD1
                    1 => REDUMP_ISO_LENGTH[1], // XGD2w0
                    2 => REDUMP_ISO_LENGTH[2], // XGD2w1
                    3 => REDUMP_ISO_LENGTH[3], // XGD2w2
                    4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 => REDUMP_ISO_LENGTH[4], // XGD2w3+
                    14 => REDUMP_ISO_LENGTH[5], // XGD2 (Hybrid)
                    15 => REDUMP_ISO_LENGTH[6], // XGD3v0
                    16 => REDUMP_ISO_LENGTH[7], // XGD3
                    _ => 0,
                };

                // Determine intended xisoType based on video ISO length
                xisoType = videoType switch
                {
                    0 => 0, // XGD1
                    1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 => 1, // XGD2
                    14 => 2, // XGD2 (Hybrid)
                    15 or 16 => 3, // XGD3
                    _ => 0,
                };
                xisoLength = XISO_LENGTH[xisoType];

                // Create redump ISO
                using FileStream redumpFS = new(redumpPath, FileMode.Create, FileAccess.Write, FileShare.None);
                Console.WriteLine($"[INFO] Writing redump ISO to {redumpPath}");

                // Open video ISO for reading
                using FileStream videoFS = new(videoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                Console.WriteLine($"[INFO] Reading video partition from {videoPath}");

                // Write Layer 0 portion of video partition
                long l0Length = VIDEO_L0_LENGTH[videoType];
                if (!WriteBytes(videoFS, redumpFS, 0, l0Length))
                {
                    Console.WriteLine("[ERROR] Failed writing layer 0 portion of video partition.");
                    return;
                }

                // Write layer 0 padding
                long xisoOffset = XISO_OFFSET[xisoType];
                long l0Padding = xisoOffset - l0Length;
                WriteZeroes(redumpFS, -1, l0Padding);

                // Write game partition
                isoFS.Seek(0, SeekOrigin.Begin);
                if (!File.Exists(fillerPath) && !File.Exists(seedPath))
                {
                    // No filler data or seed available, write entire XISO
                    if (!WriteBytes(isoFS, redumpFS, -1, isoSize))
                    {
                        Console.WriteLine("[ERROR] Failed writing game partition.");
                        return;
                    }
                }
                else
                {
                    // Get XGD1 initial seed, if provided
                    bool knownSeed = false;
                    uint xgd1Seed = 0;
                    if (xisoType == 0 && File.Exists(seedPath))
                    {
                        FileInfo seedInfo = new(seedPath);
                        if (seedInfo.Length == 4)
                        {
                            using FileStream seedFS = new(seedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                            if (!quiet)
                                Console.WriteLine($"[INFO] Reading initial seed from {seedPath}");
                            xgd1Seed = ReadUInt(seedFS);
                            knownSeed = true;
                            Console.WriteLine("[ERROR] Currently do not support writing random filler data from seed. Soon™");
                            return;
                        }
                    }
                    else if (xisoType == 0 && File.Exists(fillerPath))
                    {
                        FileInfo seedInfo = new(fillerPath);
                        if (seedInfo.Length == 4)
                        {
                            using FileStream seedFS = new(fillerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                            if (!quiet)
                                Console.WriteLine($"[INFO] Reading initial seed from {seedPath}");
                            xgd1Seed = ReadUInt(seedFS);
                            knownSeed = true;
                            Console.WriteLine("[ERROR] Currently do not support writing random filler data from seed. Soon™");
                            return;
                        }
                    }

                    // Open filler data for reading if no seed
                    FileStream fillerFS = null!;
                    if (!knownSeed && File.Exists(fillerPath))
                    {
                        fillerFS = new(fillerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        if (!quiet)
                            Console.WriteLine($"[INFO] Reading random filler data from {fillerPath}");
                    }

                    // Parse XISO filesystem for all file extents 
                    List<(uint Start, uint End)> validRanges = GetXISORanges(isoFS, 0);
                    if (!quiet)
                    {
                        foreach (var (start, end) in validRanges)
                            Console.WriteLine($"[INFO] XISO File Extent: {start}-{end}");
                    }

                    // Write filler data interleaved with XISO
                    long currentByte = 0;
                    isoFS.Seek(0, SeekOrigin.Begin);
                    while (currentByte < xisoLength)
                    {
                        long currentSector = (currentByte + SECTOR_SIZE - 1) / SECTOR_SIZE;
                        long xisoBytes = long.MaxValue;
                        long fillerBytes = 0;

                        // Determine whether current sector is after last file extent
                        if (validRanges.Count > 0 && currentSector > validRanges[validRanges.Count - 1].End)
                        {
                            // Remainder of XISO is filler
                            fillerBytes = xisoLength - currentByte;
                        }
                        else
                        {
                            // Determine whether current sector is within a file extent or filler data
                            for (int i = 0; i < validRanges.Count; i++)
                            {
                                if (currentSector >= validRanges[i].Start && currentSector <= validRanges[i].End)
                                {
                                    // Number of bytes remaining in current file extent
                                    xisoBytes = (validRanges[i].End + 1) * SECTOR_SIZE - currentByte;
                                    break;
                                }
                                else if (currentSector < validRanges[i].Start && (i == 0 || currentSector > validRanges[i - 1].End))
                                {
                                    // Wipe until next file extent
                                    fillerBytes = validRanges[i].Start * SECTOR_SIZE - currentByte;
                                    break;
                                }
                            }
                        }

                        if (fillerBytes > 0)
                        {
                            // Write filler data
                            if (knownSeed)
                            {
                                // Generate filler data
                            }
                            else if (!WriteBytes(fillerFS, redumpFS, -1, fillerBytes))
                            {
                                Console.WriteLine("[ERROR] Failed writing random filler data.");
                                return;
                            }
                            currentByte += fillerBytes;
                            isoFS.Seek(fillerBytes, SeekOrigin.Current);
                        }
                        else
                        {
                            // Write data to XISO
                            long bytesToWrite = Math.Min(xisoBytes, xisoLength - currentByte);
                            if (!WriteBytes(isoFS, redumpFS, -1, bytesToWrite))
                            {
                                Console.WriteLine("[ERROR] Failed writing game partition (XISO).");
                                return;
                            }
                            currentByte += bytesToWrite;
                        }
                    }

                    // Close files
                    if (fillerFS != null)
                        fillerFS.Dispose();

                    // Validity check
                    if (currentByte != xisoLength)
                    {
                        Console.WriteLine("[ERROR] Unexpected error, please report this");
                        return;
                    }
                }

                // Write layer 1 padding
                long l1Length = VIDEO_L1_LENGTH[videoType];
                long l1Padding = (redumpLength - l1Length) - (xisoOffset + xisoLength);
                WriteZeroes(redumpFS, -1, l1Padding);

                // If writing system update file, stop video partition early
                long suSize = 0;
                if (File.Exists(updatePath))
                {
                    Console.WriteLine($"[INFO] Rebuilding with update file: {updatePath}");
                    FileInfo suInfo = new(updatePath);
                    suSize = suInfo.Length;
                    l1Length -= suSize + SECTOR_SIZE;
                }

                // Write layer 1 portion of video partition
                if (!WriteBytes(videoFS, redumpFS, l0Length, l1Length))
                {
                    Console.WriteLine("[ERROR] Failed writing layer 1 portion of video partition.");
                    return;
                }

                // Write system update file
                if (File.Exists(updatePath))
                {
                    // Open system update file for reading
                    using FileStream updateFS = new(updatePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    Console.WriteLine($"[INFO] Reading system update from {updatePath}");

                    // Write system update file to redump ISO
                    if (!WriteBytes(updateFS, redumpFS, 0, suSize))
                    {
                        Console.WriteLine("[ERROR] Failed writing system update file.");
                        return;
                    }

                    // Write final video partition sector
                    videoFS.Seek(-SECTOR_SIZE, SeekOrigin.End);
                    if (!WriteBytes(videoFS, redumpFS, -1, SECTOR_SIZE))
                    {
                        Console.WriteLine("[ERROR] Failed writing last sector of video partition.");
                        return;
                    }
                }

                #endregion
            }
        }
    }
}
