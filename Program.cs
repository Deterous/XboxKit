using System;
using System.IO;
using System.Text;
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
            Console.WriteLine("Redump Xbox/Xbox360 ISO <---> XISO + Video Partition (+ System Update)");
            Console.WriteLine("Usage: xboxkit.exe [-s] [-u] [-v] <input.iso> [video.iso] [system_update_file]");
            Console.WriteLine("");
            Console.WriteLine("Extraction Options:");
            Console.WriteLine("-s, --skip\t Skips creating video partition (only extract XISO)");
            Console.WriteLine("-u, --unpack\t Unpacks XGD3 video partition (separate system update file)");
            Console.WriteLine("-v, --video-only\t Skips creating game partition (only extract video ISO)");
            Console.WriteLine("Note: -s cannot be used with -u or -v");
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

            Parallel.For(0x00000000, 0xFFFFFFFF, (i, state) => // 0L, 4294967296L
            {
                bool match = true;
                //uint seed = (uint)i;
                //uint mult = FIXED_SEEDS[seed & 7];
                //uint mask = (uint)((ulong)(seed + 1) * mult) % 0xFFFFFFFB;
                //uint c = seed;
                uint a_t = 0;
                uint b_t = 0;
                uint c_t = 0;

                Seed((uint)i, ref a_t, ref b_t, ref c_t);
                for (int j = 0; j < SECTOR_SIZE; j += 2)
                {
                    //c = (uint)(((ulong)(c + 1) * mult) % 0xFFFFFFFB);
                    //ushort sample = (ushort)((c ^ mask) >> 8);

                    //if (sector[j] != (byte)sample || sector[j + 1] != (byte)(sample >> 8))
                    UInt16 sampleGenerated = (UInt16)(Value(ref a_t, ref b_t, ref c_t) >> 8);
                    byte low = (byte)(sampleGenerated & 0xff);
                    byte high = (byte)((sampleGenerated >> 8) & 0xff);

                    if ((sector[0 + j] != low) || (sector[1 + j] != high))
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    Console.WriteLine("Seed found: 0x{0:x8}", i);
                    foundSeed = (uint)i;
                    seedFound = true;
                    //System.Threading.Volatile.Write(ref foundSeed, (uint)i);
                    //System.Threading.Volatile.Write(ref seedFound, true);
                    state.Stop();
                }
            });

            outSeed = foundSeed;
            return seedFound;
        }

        private static void Seed(uint seed, ref uint a_t, ref uint b_t, ref uint c_t)
        {
            a_t = 0;
            b_t = FIXED_SEEDS[seed & 7];
            c_t = seed;
            a_t = Value(ref a_t, ref b_t, ref c_t);
        }

        private static uint Value(ref uint a_t, ref uint b_t, ref uint c_t)
        {
            UInt64 result;
            result = c_t;
            result += 1;
            result *= b_t;
            result %= 0xFFFFFFFB;
            c_t = (UInt32)(result & 0xFFFFFFFF);
            return c_t ^ a_t;
        }

        static void Main(string[] args)
        {
            // Initialize VIDEO_LENGTH array
            for (int i = 0; i < VIDEO_LENGTH.Length; i++)
                VIDEO_LENGTH[i] = VIDEO_L0_LENGTH[i] + VIDEO_L1_LENGTH[i];

            bool skipVideo = false;
            bool unpackVideo = false;
            bool onlyVideo = false;
            string isoPath = string.Empty;
            string videoPath = string.Empty;
            string updatePath = string.Empty;

            // Check arguments
            if ((args.Length == 0) || (args.Length > 5))
            {
                PrintHelp();
                return;
            }
            bool helpPrinted = false;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.Equals("--h", StringComparison.OrdinalIgnoreCase) || arg.Equals("--help", StringComparison.OrdinalIgnoreCase))
                {
                    if (!helpPrinted)
                        PrintHelp();
                }
                else if (arg.Equals("-s", StringComparison.OrdinalIgnoreCase) || arg.Equals("--skip", StringComparison.OrdinalIgnoreCase))
                {
                    if (unpackVideo)
                    {
                        Console.WriteLine("Cannot use both --unpack and --skip");
                        return;
                    }
                    else if (onlyVideo)
                    {
                        Console.WriteLine("Cannot use both --video-only and --skip");
                        //return;
                    }
                    skipVideo = true;
                }
                else if (arg.Equals("-u", StringComparison.OrdinalIgnoreCase) || arg.Equals("--unpack", StringComparison.OrdinalIgnoreCase))
                {
                    if (skipVideo)
                    {
                        Console.WriteLine("Cannot use both --skip and --unpack");
                        return;
                    }
                    unpackVideo = true;
                }
                else if (arg.Equals("-v", StringComparison.OrdinalIgnoreCase) || arg.Equals("--video-only", StringComparison.OrdinalIgnoreCase))
                {
                    if (skipVideo)
                    {
                        Console.WriteLine("Cannot use both --skip and --video-only");
                        //return;
                    }
                    onlyVideo = true;
                }
                else
                {
                    if(string.IsNullOrEmpty(isoPath))
                        isoPath = arg;
                    else if (string.IsNullOrEmpty(videoPath))
                        videoPath = arg;
                    else if (string.IsNullOrEmpty(updatePath))
                        updatePath = arg;
                    else
                    {
                        if (!helpPrinted)
                            PrintHelp();
                        return;
                    }
                }
            }

            // Determine input filenames
            string dir = Path.GetDirectoryName(isoPath);
            string filename = Path.GetFileNameWithoutExtension(isoPath);

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

            // Check that input ISO exists
            if (string.IsNullOrEmpty(isoPath) || !File.Exists(isoPath))
            {
                Console.WriteLine($"[ERROR] Invalid file path: {isoPath}");
                return;
            }
            if (string.IsNullOrEmpty(videoPath))
            {
                Console.WriteLine($"[ERROR] Invalid file path: {videoPath}");
                return;
            }

            // Compare input file size against known ISO sizes to determine XGD Type
            FileInfo isoInfo = new(isoPath);
            long isoSize = isoInfo.Length;
            int xgdType = Array.IndexOf(REDUMP_ISO_LENGTH, isoSize);
            int xisoType = Array.IndexOf(XISO_LENGTH, isoSize);
            int videoIsoType = Array.IndexOf(VIDEO_LENGTH, isoSize);
            // Unknown Mode: Invalid file size
            if (xgdType < 0 && xisoType < 0 && videoIsoType < 0)
            {
                Console.WriteLine("[ERROR] Unexpected ISO size. Your file may be trimmed or corrupt.");
                return;
            }
            // Unknown mode: (shouldn't happen)
            else if (((xgdType >= 0 ? 1 : 0) + (xisoType >= 0 ? 1 : 0) + (videoIsoType >= 0 ? 1 : 0)) >= 2)
            {
                Console.WriteLine("[ERROR] Unexpected ISO size. Report this issue");
                return;
            }
            // Mode 1: Extract XISO and video ISO from redump ISO
            else if (xgdType >= 0)
            {
                // Check that unpack flag is set if third file path is given
                if (!unpackVideo && !string.IsNullOrEmpty(updatePath))
                {
                    Console.WriteLine("[ERROR] To unpack the system update file use -u or --unpack");
                    Console.WriteLine("        or exclude the third filename to write video partition intact");
                    return;
                }

                // Check that video partition doesn't exist
                if (xgdType >= 0 && File.Exists(videoPath))
                {
                    Console.WriteLine($"[ERROR] File already exists: {videoPath}");
                    return;
                }

                // Compare PVD against known PVDs to determine wave
                int? wave = null;
                if (xgdType == 4)
                {
                    try
                    {
                        using FileStream fs = new(isoPath, FileMode.Open, FileAccess.Read);
                        fs.Seek(0x832D, SeekOrigin.Begin);
                        byte[] pvd = new byte[16];
                        int bytesRead = fs.Read(pvd, 0, pvd.Length);
                        if (bytesRead == 16)
                        {
                            string pvdString = Encoding.ASCII.GetString(pvd);
                            wave = Array.IndexOf(WAVE_PVD, pvdString);
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

                int videoType = xgdType switch
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

                // Write layer 0 portion of video partition
                using FileStream isoFS = new(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                Console.WriteLine($"[INFO] Reading redump ISO from {isoPath}");
                long numBytes = 0;
                byte[] buf = new byte[64 * SECTOR_SIZE];
                if (!skipVideo)
                {
                    long l0Length = VIDEO_L0_LENGTH[videoType];
                    using FileStream videoFS = new(videoPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    Console.WriteLine($"[INFO] Writing video partition to {videoPath}");
                    isoFS.Seek(0, SeekOrigin.Begin);
                    numBytes = 0;
                    while (numBytes < l0Length)
                    {
                        int bytesRead = isoFS.Read(buf, 0, (int)Math.Min(buf.Length, l0Length - numBytes));
                        if (bytesRead == 0)
                            break;

                        videoFS.Write(buf, 0, bytesRead);
                        numBytes += bytesRead;
                    }
                    if (numBytes != l0Length)
                    {
                        Console.WriteLine("[ERROR] Failed reading video partition.");
                        return;
                    }

                    // Write layer 1 portion of video partition
                    long l1Length = VIDEO_L1_LENGTH[videoType];
                    isoFS.Seek(isoSize - l1Length, SeekOrigin.Begin);
                    numBytes = 0;
                    while (numBytes < l1Length)
                    {
                        int bytesRead = isoFS.Read(buf, 0, (int)Math.Min(buf.Length, l1Length - numBytes));
                        if (bytesRead == 0)
                            break;

                        videoFS.Write(buf, 0, bytesRead);
                        numBytes += bytesRead;
                    }
                    if (numBytes != l1Length)
                    {
                        Console.WriteLine("[ERROR] Failed reading video partition.");
                        return;
                    }
                }
                else
                {
                    Console.WriteLine("[INFO] Skipping video partition creation");
                }

                // Determine size of output XISO
                long outputXISOType = xgdType switch
                {
                    0 => 0, // XGD1
                    1 or 2 or 3 or 4 => 1, // XGD2
                    5 => 2, // XGD2 (Hybrid)
                    6 or 7 => 3, // XGD3
                    _ => -1,
                };
                if (outputXISOType == -1)
                {
                    Console.WriteLine("[ERROR] Unexpected ISO size. Is this a valid redump ISO?");
                    return;
                }

                // Get XGD1 Version
                if (outputXISOType == 0)
                {
                    isoFS.Seek(XISO_OFFSET[outputXISOType] + 0x10800, SeekOrigin.Begin);
                    byte[] magic = new byte[XDVDFS_MAGIC.Length];
                    int magicLength = XDVDFS_MAGIC.Length;
                    numBytes = 0;
                    while (numBytes < magicLength)
                    {
                        int bytesRead = isoFS.Read(magic, 0, (int)Math.Min(magic.Length, magicLength - numBytes));
                        if (bytesRead == 0)
                            break;

                        numBytes += bytesRead;
                    }
                    if (numBytes != magicLength)
                    {
                        Console.WriteLine("[ERROR] Failed reading XGD1 XDVDFS.");
                        return;
                    }
                    if (!SequenceEqual(magic, XDVDFS_MAGIC))
                    {
                        Console.WriteLine("[ERROR] Invalid data in XDVDFS volume descriptor.");
                        return;
                    }

                    // Determine XGD1 wave
                    byte[] nextBuf = new byte[8];
                    isoFS.Seek(XISO_OFFSET[outputXISOType] + 0x10820, SeekOrigin.Begin);
                    numBytes = 0;
                    while (numBytes < nextBuf.Length)
                    {
                        int bytesRead = isoFS.Read(nextBuf, 0, (int)Math.Min(nextBuf.Length, nextBuf.Length - numBytes));
                        if (bytesRead == 0)
                            break;

                        numBytes += bytesRead;
                    }
                    if (numBytes != nextBuf.Length)
                    {
                        Console.WriteLine("[ERROR] Failed reading XGD1 XDVDFS volume descriptor.");
                        return;
                    }
                    int versionOffset = 0x10824;
                    if (SequenceEqual(nextBuf, new byte[8]))
                        versionOffset += 0x10;

                    byte[] versionBuf = new byte[2];
                    isoFS.Seek(XISO_OFFSET[outputXISOType] + versionOffset, SeekOrigin.Begin);
                    numBytes = 0;
                    while (numBytes < versionBuf.Length)
                    {
                        int bytesRead = isoFS.Read(versionBuf, 0, (int)Math.Min(versionBuf.Length, versionBuf.Length - numBytes));
                        if (bytesRead == 0)
                            break;

                        numBytes += bytesRead;
                    }
                    if (numBytes != 2)
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
                    {
                        Console.WriteLine($"[INFO] XGD1 Version: {version}");
                    }

                    Console.WriteLine("Guessing seed...");
                    isoFS.Seek(XISO_OFFSET[outputXISOType], SeekOrigin.Begin);
                    byte[] firstXISOSector = new byte[SECTOR_SIZE];
                    numBytes = 0;
                    while (numBytes < SECTOR_SIZE)
                    {
                        int bytesRead = isoFS.Read(nextBuf, 0, (int)Math.Min(nextBuf.Length, SECTOR_SIZE - numBytes));
                        if (bytesRead == 0)
                            break;

                        numBytes += bytesRead;
                    }
                    if (numBytes != SECTOR_SIZE)
                    {
                        Console.WriteLine("[ERROR] Failed reading first XISO sector");
                        return;
                    }
                    if (GuessSeed(firstXISOSector, out uint seed))
                    {
                        if (version <= 4830)
                            Console.WriteLine($"[INFO] Found seed: {seed:X8}");
                        else
                            Console.WriteLine($"[INFO] RC4? But found seed: {seed:X8}");
                    }
                    else
                    {
                        if (version < 4721)
                            Console.WriteLine("[INFO Could not determine seed");
                        if (version < 5000)
                            Console.WriteLine("[INFO] Could not determine seed, RC4?");
                        else
                            Console.WriteLine("[INFO] This disc has RC4, cannot determine seed.");
                        Console.WriteLine($"[INFO] Seed: {seed:X8}");
                    }
                }

                // Don't create XISO if only extracting video partition
                if (!onlyVideo)
                {

                    // Write XISO to file
                    using FileStream xisoFS = new(xisoPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    Console.WriteLine($"[INFO] Writing game partition to {xisoPath}");
                    isoFS.Seek(XISO_OFFSET[outputXISOType], SeekOrigin.Begin);
                    long xisoLength = XISO_LENGTH[outputXISOType];
                    numBytes = 0;
                    while (numBytes < xisoLength)
                    {
                        int bytesRead = isoFS.Read(buf, 0, (int)Math.Min(buf.Length, xisoLength - numBytes));
                        if (bytesRead == 0)
                            break;

                        xisoFS.Write(buf, 0, bytesRead);
                        numBytes += bytesRead;
                    }
                    if (numBytes != xisoLength)
                    {
                        Console.WriteLine("[ERROR] Failed writing game partition (XISO).");
                        return;
                    }
                }

                // If XGD3, try extract system update file from video partition
                if (outputXISOType == 3 && unpackVideo)
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
                        numBytes = 0;
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
            // Mode 2: Combine XISO and video ISO into redump ISO
            else if (xisoType >= 0)
            {
                // Check that video partition exists
                if (xisoType >= 0 && !File.Exists(videoPath))
                {
                    Console.WriteLine($"[ERROR] Invalid file path: {videoPath}");
                    Console.WriteLine("Provide a file path to the video partition to rebuild the redump ISO.");
                    return;
                }
                // Check that update file exists, if needed
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
            // Mode 3: Extract system update file from video ISO
            else if (videoIsoType >= 0)
            {
                // Check that no other file paths are given
                if (!string.IsNullOrEmpty(videoPath) || !string.IsNullOrEmpty(updatePath))
                {
                    Console.WriteLine("[ERROR] To combine XISO and Video ISO, provide XISO path first");
                    Console.WriteLine("        To extract system update from video, provide only one ISO path");
                }

                // Must explicitly ask to extract system update
                if (!unpackVideo)
                {
                    Console.WriteLine("[ERROR] Use -u flag to extract system update from video partition.");
                    return;
                }
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
