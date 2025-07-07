using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XboxKit
{
    internal class Program
    {
        static readonly int SECTOR_SIZE = 2048;
        static readonly byte[] FILLER = Encoding.ASCII.GetBytes("ABCDABCDABCDABCD");
        static readonly byte[] XDVDFS_MAGIC = Encoding.ASCII.GetBytes("XBOX_DVD_LAYOUT_TOOL_SIG");
        static readonly uint[] FIXED_SEEDS = { 0x52F690D5, 0x534D7DDE, 0x5B71A70F, 0x66793320, 0x9B7E5ED5, 0xA465265E, 0xA53F1D11, 0xB154430F };
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

        // Print help for invalid command
        static void PrintHelp()
        {
            Console.WriteLine("XboxKit (c) Deterous 2024-2025");
            Console.WriteLine("");
            Console.WriteLine("Usage: xboxkit.exe [options] <input.iso> [video.iso] [filler_data] [system_update_file]");
            Console.WriteLine("");
            Console.WriteLine("Rebuild mode: Combine input files (no flags)");
            Console.WriteLine("Extract mode: Use flags (optional paths are used for output file names)");
            Console.WriteLine("-s, --save-filler\t Extracts XISO filler data to a separate file");
            Console.WriteLine("-t, --trim       \t Trims end of XISO (game partition)");
            Console.WriteLine("-u, --update-file\t Extracts update file from video ISO (XGD3 only)");
            Console.WriteLine("-v, --video      \t Extracts video ISO (video partition)");
            Console.WriteLine("-w, --wipe       \t Wipes filler data in XISO");
            Console.WriteLine("-x, --xiso       \t Extracts XISO (game partition)");
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

        // Brute force seed for pseudo random number generator
        static bool GuessSeed(byte[] sector, out uint outSeed)
        {
            uint foundSeed = 0;
            bool seedFound = false;

            const long MaxUInt32 = (long)uint.MaxValue + 1;
            var range = Partitioner.Create(0L, MaxUInt32);
            Parallel.ForEach(range, (chunk, state) =>
            {
                for (long i = chunk.Item1; i < chunk.Item2; i++)
                {
                    if (Volatile.Read(ref seedFound))
                        break;
                    uint seed = (uint)i;
                    uint mult = FIXED_SEEDS[seed & 7];
                    uint state_var = (uint)(((seed + 1UL) * mult) % 0xFFFFFFFB);
                    uint mask = state_var;
                    bool match = true;
                    for (int j = 0; j < SECTOR_SIZE; j += 2)
                    {
                        state_var = (uint)(((state_var + 1UL) * mult) % 0xFFFFFFFB);
                        ushort sample = (ushort)((state_var ^ mask) >> 8);
                        if (sector[j] != (byte)sample || sector[j + 1] != (byte)(sample >> 8))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        Volatile.Write(ref foundSeed, seed);
                        Volatile.Write(ref seedFound, true);
                        state.Stop();
                        break;
                    }
                }
            });

            outSeed = foundSeed;
            return seedFound;
        }

        // Read uint16 from filestream
        static ushort ReadUShort(FileStream fs)
        {
            byte[] buffer = new byte[2];
            if (fs.Read(buffer, 0, 2) != 2)
                throw new EndOfStreamException("[ERROR] Failed to read from ");
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
        static void GetValidSectors(FileStream isoFS, List<uint> validSectors, long rootOffset, uint rootSize, long childOffset)
        {
            if (childOffset >= rootSize)
                return;

            long cur = XISO_OFFSET[0] + rootOffset + childOffset;
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
                GetValidSectors(isoFS, validSectors, rootOffset, rootSize, (long)leftChildOffset * 4);

            if (isDirectory)
                GetValidSectors(isoFS, validSectors, entryOffset, entrySize, 0);
            else
            {
                long fileOffset = (XISO_OFFSET[0] + entryOffset) / SECTOR_SIZE;
                long fileSize = (entrySize + SECTOR_SIZE - 1) / SECTOR_SIZE;
                for (long i = fileOffset; i < fileOffset + fileSize; i++)
                    validSectors.Add((uint)i);
            }

            if (rightChildOffset != 0)
                GetValidSectors(isoFS, validSectors, rootOffset, rootSize, (long)rightChildOffset * 4);
        }

        // Get list of valid XISO ranges
        static List<(uint, uint)> GetXISORanges(FileStream isoFS, long offset)
        {
            List<uint> validSectors = new List<uint>();
            long headerOffset = (offset) / SECTOR_SIZE;
            validSectors.Add((uint)headerOffset);
            validSectors.Add((uint)headerOffset + 1);

            isoFS.Seek(offset + 20, SeekOrigin.Begin);
            uint rootOffset = ReadUInt(isoFS);
            uint rootSize = ReadUInt(isoFS);
            GetValidSectors(isoFS, validSectors, (long)rootOffset * SECTOR_SIZE, rootSize, 0);

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
            if (offset > 0)
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
            if (offset > 0)
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

        static void Main(string[] args)
        {
            // Initialize VIDEO_LENGTH array
            for (int i = 0; i < VIDEO_LENGTH.Length; i++)
                VIDEO_LENGTH[i] = VIDEO_L0_LENGTH[i] + VIDEO_L1_LENGTH[i];

            bool help = false;
            bool extractXISO = false;
            bool extractVideo = false;
            bool extractFiller = false;
            bool trimXISO = false;
            bool wipeXISO = false;
            bool unpackVideo = false;
            string isoPath = string.Empty;
            string videoPath = string.Empty;
            string fillerPath = string.Empty;
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
                        case "--save-filler":
                            extractFiller = true;
                            break;
                        case "--trim":
                            trimXISO = true;
                            break;
                        case "--update-file":
                            unpackVideo = true;
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
                            case 's':
                                extractFiller = true;
                                break;
                            case 't':
                                trimXISO = true;
                                break;
                            case 'u':
                                unpackVideo = true;
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
            if (string.IsNullOrEmpty(isoPath) || !File.Exists(isoPath))
            {
                Console.WriteLine($"[ERROR] Invalid file path: {isoPath}");
                return;
            }

            // Determine output filenames
            if (string.IsNullOrEmpty(videoPath))
                videoPath = Path.Combine(dir, $"{filename}.video.iso");
            string xisoPath = string.Empty;
            string redumpPath = string.Empty;
            string subExtension = Path.GetExtension(filename);
            if (subExtension == ".xiso")
            {
                xisoPath = Path.Combine(dir, $"{filename}.xiso.iso");
                filename = Path.GetFileNameWithoutExtension(filename);
                redumpPath = Path.Combine(dir, $"{filename}.redump.iso");
            }
            else if (subExtension == ".redump")
            {
                redumpPath = Path.Combine(dir, $"{filename}.redump.iso");
                filename = Path.GetFileNameWithoutExtension(filename);
                xisoPath = Path.Combine(dir, $"{filename}.xiso.iso");
            }
            else
            {
                xisoPath = Path.Combine(dir, $"{filename}.xiso.iso");
                redumpPath = Path.Combine(dir, $"{filename}.redump.iso");
            }

            // Compare input ISO file size to determine file type
            FileInfo isoInfo = new(isoPath);
            long isoSize = isoInfo.Length;
            int redumpIsoType = Array.IndexOf(REDUMP_ISO_LENGTH, isoSize);
            int xisoType = Array.IndexOf(XISO_LENGTH, isoSize);
            int videoIsoType = Array.IndexOf(VIDEO_LENGTH, isoSize);
            // Unknown Mode: Invalid file size
            if (redumpIsoType < 0 && xisoType < 0 && videoIsoType < 0)
            {
                Console.WriteLine("[ERROR] Unexpected ISO size. Your file may be trimmed or corrupt.");
                return;
            }
            // Mode 1: Redump ISO as input (Wipe XISO and/or Extract XISO and/or video ISO)
            else if (redumpIsoType >= 0)
            {
                // Check that video partition doesn't exist
                if (extractVideo && File.Exists(videoPath))
                {
                    Console.WriteLine($"[ERROR] File already exists: {videoPath}");
                    return;
                }

                // Determine disc type
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

                    using FileStream videoFS = new(videoPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    Console.WriteLine($"[INFO] Writing video partition to {videoPath}");

                    // Write layer 0 portion of video partition
                    if (!WriteBytes(isoFS, videoFS, 0, VIDEO_L0_LENGTH[videoType]))
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

                // If XGD1, try brute force the filler data seed
                uint xgd1Seed;
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
                    else
                        Console.WriteLine($"[INFO] XGD1 Version: {version}");

                    // Determine XGD1 pseudo random number generator seed, if possible
                    byte[] firstXISOSector = new byte[SECTOR_SIZE];
                    if (!WriteBytes(isoFS, firstXISOSector, XISO_OFFSET[xgdType]))
                    {
                        Console.WriteLine("[ERROR] Failed reading first XISO sector");
                        return;
                    }
                    if (GuessSeed(firstXISOSector, out uint seed))
                    {
                        xgd1Seed = seed;
                        Console.WriteLine($"[INFO] Filler data seed: {seed:X8}");
                    }
                }

                // Parse XISO filesystem for all file extents 
                List<(uint Start, uint End)> validRanges = GetXISORanges(isoFS, XISO_OFFSET[xgdType] + 0x10000);
                foreach (var (start, end) in validRanges)
                    Console.WriteLine($"[INFO] File Extent: {start}-{end}");

                // Create file for game partition
                FileStream xisoFS = null!;
                if (extractXISO)
                {
                    xisoFS = new FileStream(xisoPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    Console.WriteLine($"[INFO] Writing game partition to {xisoPath}");
                }


                FileStream fillerFS = null!;
                if (extractFiller)
                {
                    if (string.IsNullOrEmpty(fillerPath))
                        fillerPath = Path.Combine(dir, $"{filename}.filler");
                    if (File.Exists(fillerPath))
                        Console.WriteLine($"[INFO] Skipping writing filler data, file already exists: {fillerPath}");
                    else
                        fillerFS = new FileStream(fillerPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    Console.WriteLine($"[INFO] Writing filler data to {fillerPath}");
                }
                isoFS.Seek(XISO_OFFSET[xgdType], SeekOrigin.Begin);
                long xisoLength = XISO_LENGTH[xgdType];
                long numBytes = 0;
                while (numBytes < xisoLength)
                {
                    long bytesUntilEndOfExtent = long.MaxValue;
                    long bytesToWipe = -1;
                    long currentByte = XISO_OFFSET[xgdType] + numBytes;
                    long currentSector = (currentByte + SECTOR_SIZE - 1) / SECTOR_SIZE;
                    bool xisoEnd = false;

                    // Determine whether current sector is after last file extent
                    if ((extractFiller || wipeXISO || trimXISO) && validRanges.Count > 0 && currentSector > validRanges[validRanges.Count - 1].End)
                    {
                        // Wipe or trim remainder of XISO
                        bytesToWipe = xisoLength - currentByte - XISO_OFFSET[xgdType];
                        if (trimXISO && !extractFiller)
                        {
                            numBytes += bytesToWipe;
                            break;
                        }
                        else if (trimXISO)
                            xisoEnd = true;
                    }
                    else if (extractFiller || wipeXISO)
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

                    // Write zeroes to XISO (unless trimming end)
                    if (extractXISO && wipeXISO && bytesToWipe > 0 && !xisoEnd)
                    {
                        byte[] zeroBuf = new byte[64 * SECTOR_SIZE];
                        long bytesWiped = 0;
                        while (bytesWiped < bytesToWipe)
                        {
                            int bytesToWrite = (int)Math.Min(zeroBuf.Length, bytesToWipe - bytesWiped);
                            xisoFS.Write(zeroBuf, 0, bytesToWrite);
                            bytesWiped += bytesToWrite;
                        }
                        if (!extractFiller)
                            isoFS.Seek(bytesWiped, SeekOrigin.Current);
                    }

                    // Write RC4 filler data to file
                    if (extractFiller && bytesToWipe > 0)
                    {
                        if (!WriteBytes(isoFS, fillerFS, -1, bytesToWipe))
                        {
                            Console.WriteLine("[ERROR] Failed writing filler data.");
                            return;
                        }
                    }

                    if (!extractXISO && bytesToWipe > 0)
                        numBytes += bytesToWipe;
                    else if (extractXISO)
                    {
                        long bytesToRead = Math.Min(bytesUntilEndOfExtent, xisoLength - numBytes);
                        if (!WriteBytes(isoFS, xisoFS, -1, bytesToRead))
                        {
                            Console.WriteLine("[ERROR] Failed writing filler data.");
                            return;
                        }
                        numBytes += bytesToRead;
                    }
                    else
                        numBytes += Math.Min(bytesUntilEndOfExtent, xisoLength - numBytes);
                }
                if (xisoFS != null)
                    xisoFS.Dispose();
                if (fillerFS != null)
                    fillerFS.Dispose();

                if (numBytes != xisoLength)
                {
                    Console.WriteLine("[ERROR] Failed writing game partition (XISO).");
                    return;
                }

                // If XGD3, try extract system update file from video partition
                if (unpackVideo && xgdType == 3)
                {
                    using FileStream videoFS = new(videoPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    long videoLength = videoFS.Length;
                    long pos = videoLength;
                    byte[] videoBuf = new byte[16];
                    while (pos > 0)
                    {
                        videoFS.Seek(pos - SECTOR_SIZE, SeekOrigin.Begin);
                        videoFS.Read(videoBuf, 0, 16);
                        if (FILLER.AsSpan().SequenceEqual(videoBuf))
                            break;

                        pos -= SECTOR_SIZE;
                    }

                    // Set update path to default if unset
                    if (string.IsNullOrEmpty(updatePath))
                        updatePath = Path.Combine(dir, "su20076000_00000000");

                    if (File.Exists(updatePath))
                        Console.WriteLine($"[INFO] Skipping unpacking, system update file already exists: {updatePath}");
                    else
                    {
                        Console.WriteLine($"[INFO] Writing system update file to {updatePath}");
                        using FileStream updateFS = new(updatePath, FileMode.Create, FileAccess.Write, FileShare.None);
                        long updateOffset = pos;
                        long updateLength = videoLength - updateOffset - SECTOR_SIZE;
                        if (!WriteBytes(videoFS, updateFS, updateOffset, updateLength))
                        {
                            Console.WriteLine("[ERROR] Failed writing system update file.");
                            return;
                        }

                        byte[] emptyArray = new byte[64 * SECTOR_SIZE];
                        numBytes = 0;
                        videoFS.Seek(updateOffset, SeekOrigin.Begin);
                        while (numBytes < updateLength)
                        {
                            int bytesToWrite = (int)Math.Min(emptyArray.Length, updateLength - numBytes);
                            if (bytesToWrite == 0)
                                break;
                            
                            videoFS.Write(emptyArray, 0, bytesToWrite);
                            numBytes += bytesToWrite;
                        }
                        if (numBytes != updateLength)
                        {
                            Console.WriteLine("[ERROR] Failed zeroing system update file in video partition.");
                            return;
                        }
                    }
                }
            }
            // Mode 2: XISO as input (Combine XISO and video ISO into redump ISO and/or wipe filler data from XISO)
            else if (xisoType >= 0)
            {
                // Check that video partition exists
                if (!wipeXISO && !File.Exists(videoPath))
                {
                    Console.WriteLine($"[ERROR] Invalid file path: {videoPath}");
                    Console.WriteLine("Provide a file path to the video partition to rebuild the redump ISO.");
                    return;
                }
                // Check that update file exists, if given
                if (!string.IsNullOrEmpty(updatePath) && !File.Exists(updatePath))
                {
                    Console.WriteLine($"[ERROR] Invalid file path: {updatePath}");
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
                    2 => REDUMP_ISO_LENGTH[2], // XGD2w0
                    3 => REDUMP_ISO_LENGTH[3], // XGD2w0
                    4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 => REDUMP_ISO_LENGTH[4], // XGD2 (Hybrid)
                    14 => REDUMP_ISO_LENGTH[5], // XGD2-Hybrid
                    15 => REDUMP_ISO_LENGTH[6], // XGD3v0
                    16 => REDUMP_ISO_LENGTH[7], // XGD3
                    _ => -1,
                };
                if (redumpLength == -1)
                {
                    Console.WriteLine("[ERROR] Unexpected video partition type");
                    return;
                }

                // Write Layer 0 portion of video partition
                long l0Length = VIDEO_L0_LENGTH[videoType];
                using FileStream videoFS = new(videoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                Console.WriteLine($"[INFO] Writing redump ISO to {redumpPath}");
                Console.WriteLine($"[INFO] Reading video partition from {videoPath}");
                using FileStream redumpFS = new(redumpPath, FileMode.Create, FileAccess.Write, FileShare.None);
                videoFS.Seek(0, SeekOrigin.Begin);
                long numBytes = 0;
                byte[] buf = new byte[64 * SECTOR_SIZE];
                while (numBytes < l0Length)
                {
                    int bytesRead = videoFS.Read(buf, 0, (int)Math.Min(buf.Length, l0Length - numBytes));
                    if (bytesRead == 0)
                        break;

                    redumpFS.Write(buf, 0, bytesRead);
                    numBytes += bytesRead;
                }
                if (numBytes != l0Length)
                {
                    Console.WriteLine("[ERROR] Failed writing layer 0 portion of video partition.");
                    return;
                }

                // Write layer 0 padding (zeroes)
                long xisoOffset = XISO_OFFSET[xisoType];
                long l0Padding = xisoOffset - l0Length;
                numBytes = 0;
                Array.Clear(buf, 0, buf.Length);
                while (numBytes < l0Padding)
                {
                    int bytesToWrite = (int)Math.Min(buf.Length, l0Padding - numBytes);
                    if (bytesToWrite == 0)
                        break;

                    redumpFS.Write(buf, 0, bytesToWrite);
                    numBytes += bytesToWrite;
                }
                if (numBytes != l0Padding)
                {
                    Console.WriteLine("[ERROR] Failed writing layer 0 padding.");
                    return;
                }

                // Write game partition
                using FileStream xisoFS = new(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                Console.WriteLine($"[INFO] Reading XISO from {isoPath}");
                xisoFS.Seek(0, SeekOrigin.Begin);
                long xisoLength = XISO_LENGTH[xisoType];
                numBytes = 0;
                while (numBytes < xisoLength)
                {
                    int bytesRead = xisoFS.Read(buf, 0, (int)Math.Min(buf.Length, xisoLength - numBytes));
                    if (bytesRead == 0)
                        break;

                    redumpFS.Write(buf, 0, bytesRead);
                    numBytes += bytesRead;
                }
                if (numBytes != xisoLength)
                {
                    Console.WriteLine("[ERROR] Failed writing game partition.");
                    return;
                }

                // Write layer 1 padding (zeroes)
                long l1Length = VIDEO_L1_LENGTH[videoType];
                long l1Padding = (redumpLength - l1Length) - (xisoOffset + xisoLength);
                numBytes = 0;
                Array.Clear(buf, 0, buf.Length);
                while (numBytes < l1Padding)
                {
                    int bytesToWrite = (int)Math.Min(buf.Length, l1Padding - numBytes);
                    if (bytesToWrite == 0)
                        break;

                    redumpFS.Write(buf, 0, bytesToWrite);
                    numBytes += bytesToWrite;
                }
                if (numBytes != l1Padding)
                {
                    Console.WriteLine("[ERROR] Failed writing layer 1 padding.");
                    return;
                }

                // If writing system update file, stop video partition early
                if (!string.IsNullOrEmpty(updatePath))
                {
                    FileInfo suInfo = new(updatePath);
                    long suSize = suInfo.Length;
                    l1Length -= suSize + SECTOR_SIZE;
                }

                // Write layer 1 portion of video partition
                videoFS.Seek(l0Length, SeekOrigin.Begin);
                numBytes = 0;
                while (numBytes < l1Length)
                {
                    int bytesRead = videoFS.Read(buf, 0, (int)Math.Min(buf.Length, l1Length - numBytes));
                    if (bytesRead == 0)
                        break;

                    redumpFS.Write(buf, 0, bytesRead);
                    numBytes += bytesRead;
                }
                if (numBytes != l1Length)
                {
                    Console.WriteLine("[ERROR] Failed writing layer 1 portion of video partition.");
                    return;
                }

                // Write system update file
                if (!string.IsNullOrEmpty(updatePath))
                {
                    FileInfo suInfo = new(updatePath);
                    long suSize = suInfo.Length;
                    using FileStream updateFS = new(updatePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    Console.WriteLine($"[INFO] Reading system update from {updatePath}");
                    updateFS.Seek(0, SeekOrigin.Begin);
                    numBytes = 0;
                    while (numBytes < suSize)
                    {
                        int bytesRead = updateFS.Read(buf, 0, (int)Math.Min(buf.Length, suSize - numBytes));
                        if (bytesRead == 0)
                            break;

                        redumpFS.Write(buf, 0, bytesRead);
                        numBytes += bytesRead;
                    }
                    if (numBytes != suSize)
                    {
                        Console.WriteLine("[ERROR] Failed writing system update file.");
                        return;
                    }

                    // Write final video partition sector
                    videoFS.Seek(-SECTOR_SIZE, SeekOrigin.End);
                    numBytes = 0;
                    while (numBytes < SECTOR_SIZE)
                    {
                        int bytesRead = videoFS.Read(buf, 0, (int)Math.Min(buf.Length, SECTOR_SIZE - numBytes));
                        if (bytesRead == 0)
                            break;

                        redumpFS.Write(buf, 0, bytesRead);
                        numBytes += bytesRead;
                    }
                    if (numBytes != SECTOR_SIZE)
                    {
                        Console.WriteLine("[ERROR] Failed writing last sector of video partition.");
                        return;
                    }
                }
            }
            // Mode 3: Video ISO as input (Extract system update file from video ISO)
            else if (videoIsoType >= 0)
            {
                // Check that no other file paths are given
                if (!string.IsNullOrEmpty(videoPath) || !string.IsNullOrEmpty(updatePath))
                {
                    Console.WriteLine("[ERROR] To combine XISO and Video ISO, provide XISO path first");
                    Console.WriteLine("        To extract system update from video, provide only one ISO path");
                }

                // Check that user explicitly asks to extract system update
                if (!unpackVideo)
                {
                    Console.WriteLine("[ERROR] Use -u flag to extract system update from video partition.");
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
                long videoLength = videoFS.Length;
                long pos = videoLength;
                byte[] videoBuf = new byte[16];
                while (pos > 0)
                {
                    videoFS.Seek(pos - SECTOR_SIZE, SeekOrigin.Begin);
                    videoFS.Read(videoBuf, 0, 16);
                    if (FILLER.AsSpan().SequenceEqual(videoBuf))
                        break;

                    pos -= SECTOR_SIZE;
                }

                // Set update path to default if unset
                if (!string.IsNullOrEmpty(updatePath))
                    updatePath = Path.Combine(dir, "su20076000_00000000");

                if (File.Exists(updatePath))
                    Console.WriteLine($"[INFO] Skipping unpacking, system update file already exists: {updatePath}");
                else
                {
                    Console.WriteLine($"[INFO] Writing system update file to {updatePath}");
                    using FileStream updateFS = new(updatePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    long updateOffset = pos;
                    long updateLength = videoLength - updateOffset - SECTOR_SIZE;
                    byte[] buf = new byte[64 * SECTOR_SIZE];
                    int numBytes = 0;
                    videoFS.Seek(updateOffset, SeekOrigin.Begin);
                    while (numBytes < updateLength)
                    {
                        int bytesRead = videoFS.Read(buf, 0, (int)Math.Min(buf.Length, updateLength - numBytes));
                        if (bytesRead == 0)
                            break;

                        updateFS.Write(buf, 0, bytesRead);
                        numBytes += bytesRead;
                    }
                    if (numBytes != updateLength)
                    {
                        Console.WriteLine("[ERROR] Failed writing system update file.");
                        return;
                    }

                    byte[] emptyArray = new byte[64 * SECTOR_SIZE];
                    numBytes = 0;
                    videoFS.Seek(updateOffset, SeekOrigin.Begin);
                    while (numBytes < updateLength)
                    {
                        int bytesToWrite = (int)Math.Min(buf.Length, updateLength - numBytes);
                        if (bytesToWrite == 0)
                            break;
                        
                        videoFS.Write(emptyArray, 0, bytesToWrite);
                        numBytes += bytesToWrite;
                    }
                    if (numBytes != updateLength)
                    {
                        Console.WriteLine("[ERROR] Failed zeroing system update file in video partition.");
                        return;
                    }
                }
            }
        }
    }
}
