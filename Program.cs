using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace XboxKit
{
    internal class Program
    {
        // XISO Types:                              XGD1,    XGD2,   XGD2-Hybrid,    XGD3
        internal static readonly long[] XISO_OFFSET = [0x18300000, 0xFD90000, 0x89D80000, 0x2080000];
        internal static readonly long[] XISO_LENGTH = [0x1A2DB0000, 0x1B3880000, 0xBF8A0000, 0x204510000];
        // Redump ISO Types:                               XGD1,      XGD2w0,      XGD2w1,      XGD2w2,     XGD2w3+, XGD2-Hybrid,      XGD3v0,     XGD3
        internal static readonly long[] REDUMP_ISO_LENGTH = [0x1D26A8000, 0x1D3301800, 0x1D2FEF800, 0x1D3082000, 0x1D3390000, 0x1D31A0000, 0x208E05800, 0x208E03800];
        // Video Partition Types:                   XGD1,  XGD2w0,   XGD2w1,  XGD2w2,   XGD2w3,    XGD2w4-7,   XGD2w8-9, XGD2w10-12,  XGD2w13, XGD2w14-15, XGD2w16,  XGD2w17-18, XGD2w19,  XGD2w20,  XGD2-Hybrid,  XGD3-beta   XGD3v0,    XGD3
        internal static readonly long[] VIDEO_L0_LENGTH = [0xD58000, 0xA8000, 0x548000, 0x438000, 0x4BB0000, 0x56C0000, 0x5460000, 0x5BA0000, 0x5C10000, 0x55D0000, 0x55C0000, 0x8A40000, 0x8A90000, 0x8E80000, 0x4B1D0000, 0x1878000, 0x1880000, 0x1880000];
        internal static readonly long[] VIDEO_L1_LENGTH = [0x50000, 0x9800, 0x197800, 0x11A000, 0x4BA0000, 0x56B0000, 0x5450000, 0x5B90000, 0x5C00000, 0x55C0000, 0x55B0000, 0x8A30000, 0x8A80000, 0x8E70000, 0x4AFD0000, 0x186D800, 0x1875800, 0x1873800];
        internal static readonly long[] VIDEO_LENGTH = new long[VIDEO_L0_LENGTH.Length];

        // Print help text to console
        static void PrintHelp()
        {
            Console.WriteLine("XboxKit (c) Deterous 2024-2026");
            Console.WriteLine("");
            Console.WriteLine("Usage: xboxkit.exe [options] <input.iso> [files]");
            Console.WriteLine("");
            Console.WriteLine("Rebuild mode: Don't use any options (combines input files)");
            Console.WriteLine("Extract mode: Use one or more options (splits input file)");
            Console.WriteLine("");
            Console.WriteLine("Batch options (for redump ISO):");
            Console.WriteLine("  -a, --all       All options for lossless XISO extraction (-rstuvwx)");
            Console.WriteLine("  -b, --best      Create trimmed/wiped XISO only (-twx)");
            Console.WriteLine("  -c, --compress  Options for lossless ZArchive compression (-puvz)");
            Console.WriteLine("");
            Console.WriteLine("Manual options:");
            Console.WriteLine("  -m, --metadata  Extract metadata in the form of an XRD file");
            Console.WriteLine("  -n, --no        Assume no (stops at warnings, never overwrites)");
            Console.WriteLine("  -o, --output    Extracts and outputs the game files from the XISO");
            Console.WriteLine("  -p, --petrify   Extracts XDVDFS skeleton (XISO with zeroed files)");
            Console.WriteLine("  -q, --quiet     Don't print INFO messages to console");
            Console.WriteLine("  -r, --random    Extracts random filler data to a separate file");
            Console.WriteLine("  -s, --seed      Extracts RNG seed used for XGD1 filler");
            Console.WriteLine("  -t, --trim      Trims end of XISO (game partition)");
            Console.WriteLine("  -u, --update    Extracts update file from video ISO (XGD3 only)");
            Console.WriteLine("  -v, --video     Extracts video ISO (video partition)");
            Console.WriteLine("  -w, --wipe      Wipes random filler data in XISO");
            Console.WriteLine("  -x, --xiso      Extracts XDVDFS ISO (game partition)");
            Console.WriteLine("  -y, --yes       Assume yes (ignores warnings, always overwrites)");
            Console.WriteLine("  -z, --zar       Creates ZArchive of game files");
        }

        static void Main(string[] args)
        {
            #region Initial Setup

            // Initialize VIDEO_LENGTH array at run-time
            for (int i = 0; i < VIDEO_LENGTH.Length; i++)
                VIDEO_LENGTH[i] = VIDEO_L0_LENGTH[i] + VIDEO_L1_LENGTH[i];

            // Initialize program options
            bool help = false;
            bool extractXRD = false;
            bool assumeNo = false;
            bool outputFiles = false;
            bool extractSkeleton = false;
            bool quiet = false;
            bool extractFiller = false;
            bool extractSeed = false;
            bool trimXISO = false;
            bool extractUpdate = false;
            bool extractVideo = false;
            bool wipeXISO = false;
            bool extractXISO = false;
            bool assumeYes = false;
            bool extractZAR = false;
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
                            extractFiller = true;
                            extractSeed = true;
                            trimXISO = true;
                            extractUpdate = true;
                            extractVideo = true;
                            wipeXISO = true;
                            extractXISO = true;
                            break;
                        case "--best":
                            trimXISO = true;
                            wipeXISO = true;
                            extractXISO = true;
                            break;
                        case "--compress":
                            extractSkeleton = true;
                            extractUpdate = true;
                            extractVideo = true;
                            extractZAR = true;
                            break;
                        case "--metadata":
                            extractXRD = true;
                            break;
                        case "--no":
                            assumeNo = true;
                            break;
                        case "--output":
                            outputFiles = true;
                            break;
                        case "--petrify":
                            extractSkeleton = true;
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
                        case "--yes":
                            assumeYes = true;
                            break;
                        case "--zar":
                            extractZAR = true;
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
                                extractFiller = true;
                                extractSeed = true;
                                trimXISO = true;
                                extractUpdate = true;
                                extractVideo = true;
                                wipeXISO = true;
                                extractXISO = true;
                                break;
                            case 'b':
                                trimXISO = true;
                                wipeXISO = true;
                                extractXISO = true;
                                break;
                            case 'c':
                                extractSkeleton = true;
                                extractUpdate = true;
                                extractVideo = true;
                                extractZAR = true;
                                break;
                            case "m":
                                extractXRD = true;
                                break;
                            case 'n':
                                assumeNo = true;
                                break;
                            case 'o':
                                outputFiles = true;
                                break;
                            case 'p':
                                extractSkeleton = true;
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
                            case 'y':
                                assumeYes = true;
                                break;
                            case 'z':
                                extractZAR = true;
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
            if (help || filePaths.Count == 0)
            {
                PrintHelp();
                return;
            }

            // TODO: Set isoPath to (redump ISO > XISO > video ISO) regardless of order
            string isoPath = filePaths[0];
                if (string.IsNullOrEmpty(isoPath) || !File.Exists(isoPath))
            {
                Console.WriteLine($"[ERROR] Invalid file path: {isoPath}");
                return;
            }

            // TODO: Account for isoPath being .video.iso or .redump.iso or .skeleton.xiso
            string dir = Path.GetDirectoryName(isoPath);
            string filename = Path.GetFileNameWithoutExtension(isoPath);
            string extension = Path.GetExtension(isoPath);

            // TODO: Add SabreTools.Serialization
            if (extractZAR || outputFiles || extension == ".zar")
            {
                Console.WriteLine("This feature is coming soon!");
                return;
            }

            // TODO: Prefer just .iso if it doesn't already exist?
            string redumpPath = Path.Combine(dir, $"{filename}.redump.iso");
            string skeletonPath = Path.Combine(dir, $"{filename}.skeleton.xiso");
            string fillerPath = Path.Combine(dir, $"{filename}.filler");
            string seedPath = Path.Combine(dir, $"{filename}.seed");
            string sectorsTXTPath = Path.Combine(dir, "sectors.txt");
            string updatePath = Path.Combine(dir, "su20076000_00000000");
            string videoPath = Path.Combine(dir, $"{filename}.video.iso");
            string xisoPath = Path.Combine(dir, $"{filename}.xiso");
            string zarPath = Path.Combine(dir, $"{filename}.zar");

            // Parse additional input files
            // TODO: Don't rely on the order of the input files, detect instead
            if (filePaths.Count > 1)
                videoPath = filePaths[1];
            if (filePaths.Count > 2)
                fillerPath = filePaths[2];
            if (filePaths.Count > 3)
                updatePath = filePaths[3];

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

                #region Validation

                // Must be doing something
                if (!outputFiles && !extractXRD && !extractSkeleton && !extractFiller && !extractSeed && !extractUpdate && !extractVideo && !extractXISO && !extractZAR)
                {
                    Console.WriteLine("[ERROR] Redump ISO provided with no options, nothing to do");
                    Console.WriteLine("");
                    PrintHelp();
                    return;
                }

                // Can't create both XISO and XISO Skeleton
                if (extractXISO && extractSkeleton)
                {
                    Console.WriteLine("[ERROR] Cannot create both XISO (-x) and XISO Skeleton (-p)");
                    Console.WriteLine("        Skeleton zeroes game files, typically used with -o or -z");
                    Console.WriteLine("");
                    return;
                }

                // Must extract video if also extracting SU
                if (!assumeYes && extractUpdate && !extractVideo)
                {
                    Console.WriteLine("[ERROR] Extracting update (-u) implies extract video (-v)");
                    if (assumeNo)
                        return;
                    Console.WriteLine($"Would you like to also extract Video? (Y/N)");
                    string response = Console.ReadLine()?.ToUpper();
                    if (response != "Y" && response != "YES")
                        return;
                }

                // Check option combination is valid
                if (!assumeYes && wipeXISO && !(extractXISO || extractSkeleton) && (assumeNo || !quiet))
                {
                    Console.WriteLine("[INFO] Wiping XISO option (-w) does nothing without extracting XISO (-x) or skeleton (-p)");
                    if (assumeNo)
                        return;
                }
                if (!assumeYes && trimXISO && !(extractXISO || extractSkeleton) && (assumeNo || !quiet))
                {
                    Console.WriteLine("[INFO] Trimming XISO option (-t) does nothing without extracting XISO (-x) or skeleton (-p)");
                    if (assumeNo)
                        return;
                }
                if (!assumeYes && (extractXISO || extractSkeleton) && extractFiller && !wipeXISO && (assumeNo || !quiet))
                {
                    Console.WriteLine("[INFO] Cannot write filler data without wiping XISO");
                    Console.WriteLine("       For now, use -w with -s");
                    if (assumeNo)
                        return;
                }

                // Check that files don't already exist
                if (!assumeYes && extractXISO && File.Exists(xisoPath))
                {
                    if (assumeNo)
                    {
                        Console.WriteLine($"[ERROR] File already exists: {xisoPath}");
                        return;
                    }
                    else
                    {
                        Console.WriteLine($"[WARNING] File already exists: {xisoPath}");
                        Console.WriteLine($"Would you like to overwrite? (Y/N)");
                        string response = Console.ReadLine()?.ToUpper();
                        if (response != "Y" && response != "YES")
                            return;
                    }
                }
                if (!assumeYes && extractVideo && File.Exists(videoPath))
                {
                    if (assumeNo)
                    {
                        Console.WriteLine($"[ERROR] File already exists: {videoPath}");
                        return;
                    }
                    else
                    {
                        Console.WriteLine($"[WARNING] File already exists: {videoPath}");
                        Console.WriteLine($"Would you like to overwrite? (Y/N)");
                        string response = Console.ReadLine()?.ToUpper();
                        if (response != "Y" && response != "YES")
                            return;
                    }
                }
                if (!assumeYes && extractFiller && File.Exists(fillerPath))
                {
                    if (assumeNo)
                    {
                        Console.WriteLine($"[ERROR] File already exists: {fillerPath}");
                        return;
                    }
                    else
                    {
                        Console.WriteLine($"[WARNING] File already exists: {fillerPath}");
                        Console.WriteLine($"Would you like to overwrite? (Y/N)");
                        string response = Console.ReadLine()?.ToUpper();
                        if (response != "Y" && response != "YES")
                            return;
                    }
                }
                if (!assumeYes && extractUpdate && File.Exists(updatePath))
                {
                    if (assumeNo)
                    {
                        Console.WriteLine($"[ERROR] File already exists: {updatePath}");
                        return;
                    }
                    else
                    {
                        Console.WriteLine($"[WARNING] File already exists: {updatePath}");
                        Console.WriteLine($"Would you like to overwrite? (Y/N)");
                        string response = Console.ReadLine()?.ToUpper();
                        if (response != "Y" && response != "YES")
                            return;
                    }
                }
                if (!assumeYes && extractSeed && File.Exists(seedPath))
                {
                    if (assumeNo)
                    {
                        Console.WriteLine($"[ERROR] File already exists: {seedPath}");
                        return;
                    }
                    else
                    {
                        Console.WriteLine($"[WARNING] File already exists: {seedPath}");
                        Console.WriteLine($"Would you like to overwrite? (Y/N)");
                        string response = Console.ReadLine()?.ToUpper();
                        if (response != "Y" && response != "YES")
                            return;
                    }
                }

                #endregion

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
                if (!quiet) Console.WriteLine($"[INFO] Reading redump ISO from {isoPath}");
                using FileStream isoFS = new(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);

                // Extract rebuild data
                if (extractXRD)
                {
                    // Create file for XRD
                    if (!quiet) Console.WriteLine($"[INFO] Writing metadata XRD to {xrdPath}");
                    using FileStream xrdFS = new(xrdPath, FileMode.Create, FileAccess.Write, FileShare.None);

                    if (!quiet) Console.WriteLine("[INFO] Extracting XRD...");
                    ExtractRebuildData(isoFS, xrdFS, xgdType);
                }

                // Extract video partition
                if (extractVideo)
                {
                    // Compare PVD creation datetime against known datetimes to determine wave
                    int videoType = GetVideoType(isoFS, redumpIsoType);
                    if (videoType == -1)
                    {
                        Console.WriteLine("[ERROR] Unexpected video partition. Cannot determine wave");
                        return;
                    }

                    // Create file for video partition
                    if (!quiet) Console.WriteLine($"[INFO] Writing video partition to {videoPath}");
                    using FileStream videoFS = new(videoPath, FileMode.Create, FileAccess.Write, FileShare.None);

                    // Write layer 0 portion of video partition
                    long l0Length = VIDEO_L0_LENGTH[videoType];
                    if (!Utils.WriteBytes(isoFS, videoFS, 0, l0Length))
                    {
                        Console.WriteLine($"[ERROR] Failed writing video partition.");
                        return;
                    }

                    // Write layer 1 portion of video partition
                    long l1Length = VIDEO_L1_LENGTH[videoType];
                    if (!Utils.WriteBytes(isoFS, videoFS, isoSize - l1Length, l1Length))
                    {
                        Console.WriteLine("[ERROR] Failed reading video partition.");
                        return;
                    }
                }

                // Extract system update file from XGD3 video partition
                if (extractUpdate && xgdType == 3)
                {
                    if (!quiet) Console.WriteLine($"[INFO] Writing system update file to {updatePath}");
                    if (!quiet) Console.WriteLine($"[INFO] Zeroing system update file in {videoPath}");
                    if (!XDVDFS.ExtractSU(videoPath, updatePath))
                    {
                        Console.WriteLine($"[ERROR] Failed writing system update file.");
                        return;
                    }
                }

                // If XGD1, try brute force the filler data seed
                if (extractSeed && xgdType == 0)
                {
                    // Validate XGD1 magic bytes
                    byte[] magic = new byte[XDVDFS.MAGIC2.Length];
                    if (!Utils.WriteBytes(isoFS, magic, XISO_OFFSET[xgdType] + 0x10800))
                    {
                        Console.WriteLine("[ERROR] Failed reading XGD1 XDVDFS.");
                        return;
                    }
                    if (!magic.SequenceEqual(XDVDFS.MAGIC2))
                    {
                        Console.WriteLine("[ERROR] Invalid data in XDVDFS volume descriptor.");
                        return;
                    }

                    // Determine version offset
                    byte[] nextBuf = new byte[8];
                    if (!Utils.WriteBytes(isoFS, nextBuf, XISO_OFFSET[xgdType] + 0x10820))
                    {
                        Console.WriteLine("[ERROR] Failed reading XGD1 XDVDFS volume descriptor.");
                        return;
                    }
                    int versionOffset = 0x10824;
                    if (nextBuf.SequenceEqual(new byte[8]))
                        versionOffset += 0x10;

                    // Determine XGD1 version
                    byte[] versionBuf = new byte[2];
                    if (!Utils.WriteBytes(isoFS, versionBuf, XISO_OFFSET[xgdType] + versionOffset))
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
                    if (!quiet) Console.WriteLine($"[INFO] XGD1 Version: {version}");

                    // Determine XGD1 pseudo random number generator seed, if possible
                    byte[] firstXISOSector = new byte[XDVDFS.SECTOR_SIZE * 2];
                    if (!Utils.WriteBytes(isoFS, firstXISOSector, XISO_OFFSET[xgdType]))
                    {
                        Console.WriteLine("[ERROR] Failed reading first XISO sector");
                        return;
                    }
                    if (XboxPRNG.TryGetSeed(firstXISOSector, out uint seed))
                    {
                        if (!quiet) Console.WriteLine($"[INFO] Filler data seed: {seed:X8}");
                        using FileStream seedFS = new(seedPath, FileMode.Create, FileAccess.Write, FileShare.None);
                        byte[] seedBytes = BitConverter.GetBytes(seed);
                        seedFS.Write(seedBytes, 0, seedBytes.Length);
                        if (!quiet) Console.WriteLine($"[INFO] Writing filler data to {seedPath}");
                    }
                }
                else if (extractSeed)
                {
                    if (!quiet) Console.WriteLine($"[INFO] Cannot extract seed from Xbox 360 discs");
                }

                // Quit early if we're not extracting data from game partition
                if (!extractXISO && !extractFiller && !outputFiles && !extractZAR && !extractSkeleton)
                    return;

                // Parse XISO filesystem for all file extents 
                var validRanges = XDVDFS.GetXISORanges(isoFS, XISO_OFFSET[xgdType], quiet);
                if (!quiet)
                    foreach (var (start, end) in validRanges.All) Console.WriteLine($"[INFO] XISO File Extent: {start}-{end}");

                // Create file for game partition
                FileStream xisoFS = null!;
                if (extractXISO)
                {
                    if (!quiet) Console.WriteLine($"[INFO] Writing game partition to {xisoPath}");
                    xisoFS = new FileStream(xisoPath, FileMode.Create, FileAccess.Write, FileShare.None);
                }
                else if (extractSkeleton)
                {
                    if (!quiet) Console.WriteLine($"[INFO] Writing XISO skeleton to {skeletonPath}");
                    xisoFS = new FileStream(skeletonPath, FileMode.Create, FileAccess.Write, FileShare.None);
                }

                // Create file for filler data
                FileStream fillerFS = null!;
                if (extractFiller)
                {
                    if (!quiet) Console.WriteLine($"[INFO] Writing random filler data to {fillerPath}");
                    fillerFS = new FileStream(fillerPath, FileMode.Create, FileAccess.Write, FileShare.None);
                }

                // Create file for ZAR
                FileStream zarFS = null!;
                if (extractZAR)
                {
                    if (!quiet) Console.WriteLine($"[INFO] Writing ZArchive to {zarPath}");
                    zarFS = new FileStream(zarPath, FileMode.Create, FileAccess.Write, FileShare.None);
                }

                // Process XISO
                isoFS.Seek(XISO_OFFSET[xgdType], SeekOrigin.Begin);
                long xisoLength = XISO_LENGTH[xgdType];
                long numBytes = 0;
                while (numBytes < xisoLength)
                {
                    long currentByte = XISO_OFFSET[xgdType] + numBytes;
                    long currentSector = (currentByte + XDVDFS.SECTOR_SIZE - 1) / XDVDFS.SECTOR_SIZE;
                    long bytesUntilEndOfExtent = 0;
                    long bytesToWipe = 0;
                    bool skipEnd = false;

                    // Determine whether current sector is after last file extent
                    if (validRanges.All.Count > 0 && currentSector > validRanges.All[validRanges.All.Count - 1].End)
                    {
                        // Remainder of XISO is filler
                        long bytesUntilEnd = xisoLength - numBytes;
                        if (extractFiller || wipeXISO)
                            bytesToWipe = bytesUntilEnd;
                        
                        // Trim XISO
                        if (trimXISO)
                        {
                            skipEnd = true;
                            if (!quiet) Console.WriteLine($"[INFO] Trimming XISO");
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
                        for (int i = 0; i < validRanges.All.Count; i++)
                        {
                            if (currentSector >= validRanges.All[i].Start && currentSector <= validRanges.All[i].End)
                            {
                                // Number of bytes remaining in current file extent
                                bytesUntilEndOfExtent = (validRanges.All[i].End + 1) * XDVDFS.SECTOR_SIZE - currentByte;
                                break;
                            }
                            else if (currentSector < validRanges.All[i].Start && (i == 0 || currentSector > validRanges.All[i - 1].End))
                            {
                                // Wipe until next file extent
                                bytesToWipe = validRanges.All[i].Start * XDVDFS.SECTOR_SIZE - currentByte;
                                break;
                            }
                        }
                    }

                    // Write filler data to file
                    if (extractFiller)
                    {
                        if (bytesToWipe > 0)
                        {
                            if (!Utils.WriteBytes(isoFS, fillerFS, -1, bytesToWipe))
                            {
                                Console.WriteLine($"[ERROR] Failed writing filler data.");
                                return;
                            }
                            if (!(extractXISO || extractSkeleton))
                                numBytes += bytesToWipe;
                        }
                        else if (!(extractXISO || extractSkeleton))
                        {
                            // Skip file extent
                            long bytesToEnd;
                            if (bytesUntilEndOfExtent > 0)
                                bytesToEnd = bytesUntilEndOfExtent;
                            else
                                bytesToEnd = xisoLength - numBytes;
                            isoFS.Seek(bytesToEnd, SeekOrigin.Current);
                            numBytes += bytesToEnd;
                        }
                    }

                    // Write to XISO file
                    if (extractXISO || extractSkeleton)
                    {
                        if (wipeXISO && bytesToWipe > 0 && !skipEnd)
                        {
                            // Validity check
                            if (bytesToWipe % XDVDFS.SECTOR_SIZE != 0)
                            {
                                Console.WriteLine("[ERROR] Unexpected Error 2, please report this.");
                                return;
                            }
                            // Write zeroes to XISO (unless trimming end)
                            Utils.WriteZeroes(xisoFS, -1, bytesToWipe);
                            numBytes += bytesToWipe;

                            // Move ahead in ISO file if filler was not read
                            if (!extractFiller)
                                isoFS.Seek(bytesToWipe, SeekOrigin.Current);
                        }
                        else if (!skipEnd)
                        {
                            // Determine number of bytes to write
                            long bytesToRead;
                            if (bytesToWipe > 0)
                                bytesToRead = bytesToWipe;
                            else if (bytesUntilEndOfExtent > 0)
                                bytesToRead = bytesUntilEndOfExtent;
                            else
                                bytesToRead = xisoLength - numBytes;
                                
                            // Check if current sector is a filesystem sector
                            bool is_bone = false;
                            for (int i = 0; i < validRanges.Sys.Count; i++)
                            {
                                if (currentSector >= validRanges.Sys[i].Start && currentSector <= validRanges.Sys[i].End)
                                {
                                    // Retain in skeleton
                                    is_bone = true;
                                    bytesToRead = (validRanges.Sys[i].End + 1) * XDVDFS.SECTOR_SIZE - currentByte;
                                    break;
                                }
                            }

                            if (extractXISO || is_bone)
                            {
                                // Write data to XISO
                                if (!Utils.WriteBytes(isoFS, xisoFS, -1, bytesToRead))
                                {
                                    Console.WriteLine($"[ERROR] Failed writing game partition (XISO).");
                                    return;
                                }
                            }
                            else if (extractSkeleton)
                            {
                                // Write zeroes to XISO Skeleton
                                Utils.WriteZeroes(xisoFS, -1, bytesToRead);
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
                if (zarFS != null)
                    zarFS.Dispose();

                // Validity check
                if (numBytes != xisoLength)
                {
                    Console.WriteLine("[ERROR] Unexpected Error 3, please report this");
                    return;
                }

                #endregion
            }
            else if (videoIsoType >= 0)
            {
                #region Mode 2: Video ISO as input

                #region Validation

                if (!extractUpdate)
                {
                    Console.WriteLine("[ERROR] Use -u flag to extract system update from video partition.");
                    return;
                }

                // Check for valid options
                if (!assumeYes && (assumeNo || !quiet))
                {
                    bool invalidOptions = false;
                    if (extractVideo)
                    {
                        Console.WriteLine("[INFO] Cannot extract video (-v), input file is already video ISO.");
                        invalidOptions = true;
                    }
                    if (extractXISO)
                    {
                        Console.WriteLine("[INFO] Cannot extract XISO (-x), input file is video ISO.");
                        invalidOptions = true;
                    }
                    if (extractFiller)
                    {
                        Console.WriteLine("[INFO] Cannot extract filler (-s), input file is video ISO.");
                        invalidOptions = true;
                    }
                    if (wipeXISO)
                    {
                        Console.WriteLine("[INFO] Cannot wipe XISO (-w), input file is video ISO.");
                        invalidOptions = true;
                    }
                    if (trimXISO)
                    {
                        Console.WriteLine("[INFO] Cannot trim XISO (-t), input file is video ISO.");
                        invalidOptions = true;
                    }
                    if (invalidOptions && assumeNo)
                        return;
                }

                // Check that update file doesn't already exist
                if (!assumeYes && extractUpdate && File.Exists(updatePath))
                {
                    if (assumeNo)
                    {
                        Console.WriteLine($"[ERROR] File already exists: {updatePath}");
                        return;
                    }
                    else
                    {
                        Console.WriteLine($"[WARNING] File already exists: {updatePath}");
                        Console.WriteLine($"Would you like to overwrite? (Y/N)");
                        string response = Console.ReadLine()?.ToUpper();
                        if (response != "Y" && response != "YES")
                            return;
                    }
                }

                // Check that video partition is from XGD3 disc
                if (videoIsoType != 15 && videoIsoType != 16 && videoIsoType != 17)
                {
                    Console.WriteLine("[ERROR] Can only extract su20076000_00000000 from XGD3 video partitions.");
                    return;
                }

                #endregion

                if (!quiet) Console.WriteLine($"[INFO] Writing system update file to {updatePath}");
                if (!quiet) Console.WriteLine($"[INFO] Zeroing system update file in {isoPath}");
                if (!XDVDFS.ExtractSU(isoPath, updatePath))
                {
                    Console.WriteLine($"[ERROR] Failed writing system update file.");
                    return;
                }

                #endregion
            }
            else
            {
                // Mode 3: XISO or ZAR as input

                // TODO: Validate file is an XISO (other than filesize)
                // TODO: If ZAR is input, extract

                #region Wipe XISO

                #region Validation

                // Check for invalid options
                if (!assumeYes && (assumeNo || !quiet))
                {
                    bool invalidOptions = false;
                    if (extractXISO)
                    {
                        Console.WriteLine("[INFO] Cannot extract XISO (-x), input file is already XISO.");
                        invalidOptions = true;
                    }
                    if (extractVideo)
                    {
                        Console.WriteLine("[INFO] Cannot extract video (-v), input file is XISO.");
                        invalidOptions = true;
                    }
                    if (extractUpdate)
                    {
                        Console.WriteLine("[INFO] Cannot extract update (-u), input file is XISO.");
                        invalidOptions = true;
                    }
                    if (invalidOptions && assumeNo)
                        return;
                }

                #endregion

                // Open XISO for reading
                using FileStream isoFS = new(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!quiet) Console.WriteLine($"[INFO] Reading XISO from {isoPath}");

                bool writeXISO = wipeXISO || trimXISO || extractSkeleton;
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
                    var validRanges = XDVDFS.GetXISORanges(isoFS, 0, quiet);
                    if (!quiet)
                        foreach (var (start, end) in validRanges.All) Console.WriteLine($"[INFO] XISO File Extent: {start}-{end}");

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
                        long currentSector = (currentByte + XDVDFS.SECTOR_SIZE - 1) / XDVDFS.SECTOR_SIZE;
                        long bytesUntilEndOfExtent = 0;
                        long bytesToWipe = 0;
                        bool skipEnd = false;

                        // Determine whether current sector is after last file extent
                        if (validRanges.All.Count > 0 && currentSector > validRanges.All[validRanges.All.Count - 1].End)
                        {
                            // Remainder of XISO is filler
                            long bytesUntilEnd = isoSize - currentByte;
                            if (extractFiller || wipeXISO)
                                bytesToWipe = bytesUntilEnd;
                            
                            // Trim XISO
                            if (trimXISO)
                            {
                                skipEnd = true;
                                if (!quiet) Console.WriteLine($"[INFO] Trimming XISO");
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
                            for (int i = 0; i < validRanges.All.Count; i++)
                            {
                                if (currentSector >= validRanges.All[i].Start && currentSector <= validRanges.All[i].End)
                                {
                                    // Number of bytes remaining in current file extent
                                    bytesUntilEndOfExtent = (validRanges.All[i].End + 1) * XDVDFS.SECTOR_SIZE - currentByte;
                                    break;
                                }
                                else if (currentSector < validRanges.All[i].Start && (i == 0 || currentSector > validRanges.All[i - 1].End))
                                {
                                    // Wipe until next file extent
                                    bytesToWipe = validRanges.All[i].Start * XDVDFS.SECTOR_SIZE - currentByte;
                                    break;
                                }
                            }
                        }

                        // Write filler data to file
                        if (extractFiller)
                        {
                            if (bytesToWipe > 0)
                            {
                                if (!Utils.WriteBytes(isoFS, fillerFS, -1, bytesToWipe))
                                {
                                    Console.WriteLine($"[ERROR] Failed writing filler data.");
                                    return;
                                }
                                if (!writeXISO)
                                    currentByte += bytesToWipe;
                            }
                            else if (!writeXISO)
                            {
                                // Skip file extent
                                long bytesToEnd;
                                if (bytesUntilEndOfExtent > 0)
                                    bytesToEnd = bytesUntilEndOfExtent;
                                else
                                    bytesToEnd = isoSize - currentByte;
                                isoFS.Seek(bytesToEnd, SeekOrigin.Current);
                                currentByte += bytesToEnd;
                            }
                        }

                        // Write to XISO file
                        if (writeXISO)
                        {
                            if (wipeXISO && bytesToWipe > 0 && !skipEnd)
                            {
                                // Validity check
                                if (bytesToWipe % XDVDFS.SECTOR_SIZE != 0)
                                {
                                    Console.WriteLine("[ERROR] Unexpected Error 4, please report this.");
                                    return;
                                }
                                // Write zeroes to XISO (unless trimming end)
                                Utils.WriteZeroes(xisoFS, -1, bytesToWipe);
                                currentByte += bytesToWipe;

                                // Move ahead in ISO file if filler was not read
                                if (!extractFiller)
                                    isoFS.Seek(bytesToWipe, SeekOrigin.Current);
                            }
                            else if (!skipEnd)
                            {
                                // Determine number of bytes to write
                                long bytesToRead;
                                if (bytesToWipe > 0)
                                    bytesToRead = bytesToWipe;
                                else if (bytesUntilEndOfExtent > 0)
                                    bytesToRead = bytesUntilEndOfExtent;
                                else
                                    bytesToRead = isoSize - currentByte;
                                
                                // Check if current sector is a filesystem sector
                                bool is_bone = false;
                                for (int i = 0; i < validRanges.Sys.Count; i++)
                                {
                                    if (currentSector >= validRanges.Sys[i].Start && currentSector <= validRanges.Sys[i].End)
                                    {
                                        // Retain in skeleton
                                        is_bone = true;
                                        bytesToRead = (validRanges.Sys[i].End + 1) * XDVDFS.SECTOR_SIZE - currentByte;
                                        break;
                                    }
                                }

                                if (extractSkeleton && !is_bone)
                                {
                                    // Write zeroes to XISO Skeleton
                                    Utils.WriteZeroes(xisoFS, -1, bytesToRead);
                                }
                                else
                                {
                                    // Write data to XISO
                                    if (!Utils.WriteBytes(isoFS, xisoFS, -1, bytesToRead))
                                    {
                                        Console.WriteLine($"[ERROR] Failed writing game partition (XISO).");
                                        return;
                                    }
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
                        Console.WriteLine("[ERROR] Unexpected Error 5, please report this");
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
                    Console.WriteLine("         Provide a file path to the video partition to rebuild the redump ISO.");
                    return;
                }

                // TODO: Allow for rebuilding with trimmed XISO file without filler data
                if (xisoType < 0 && !File.Exists(fillerPath) && !File.Exists(seedPath))
                {
                    Console.WriteLine("[ERROR] Unexpected XISO size. Your XISO may be trimmed or corrupt.");
                    Console.WriteLine("        Cannot rebuild redump ISO from trimmed XISO without filler or seed.");
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
                    15 or 16 => REDUMP_ISO_LENGTH[6], // XGD3-beta, XGD3v0
                    17 => REDUMP_ISO_LENGTH[7], // XGD3
                    _ => 0,
                };

                // Determine intended xisoType based on video ISO length
                xisoType = videoType switch
                {
                    0 => 0, // XGD1
                    1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 => 1, // XGD2
                    14 => 2, // XGD2 (Hybrid)
                    15 or 16 or 17 => 3, // XGD3
                    _ => 0,
                };
                long xisoLength = XISO_LENGTH[xisoType];

                // Create redump ISO
                using FileStream redumpFS = new(redumpPath, FileMode.Create, FileAccess.Write, FileShare.None);
                if (!quiet) Console.WriteLine($"[INFO] Writing redump ISO to {redumpPath}");

                // Open video ISO for reading
                using FileStream videoFS = new(videoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!quiet) Console.WriteLine($"[INFO] Reading video partition from {videoPath}");

                // Write Layer 0 portion of video partition
                long l0Length = VIDEO_L0_LENGTH[videoType];
                if (!Utils.WriteBytes(videoFS, redumpFS, 0, l0Length))
                {
                    Console.WriteLine($"[ERROR] Failed writing layer 0 portion of video partition.");
                    return;
                }

                // Write layer 0 padding
                long xisoOffset = XISO_OFFSET[xisoType];
                long l0Padding = xisoOffset - l0Length;
                Utils.WriteZeroes(redumpFS, -1, l0Padding);

                // Write game partition
                isoFS.Seek(0, SeekOrigin.Begin);
                if (!File.Exists(fillerPath) && !File.Exists(seedPath))
                {
                    // No filler data or seed available, write entire XISO
                    // TODO: Warn or error if filler is zeroed in XISO
                    if (!Utils.WriteBytes(isoFS, redumpFS, -1, isoSize))
                    {
                        Console.WriteLine($"[ERROR] Failed writing game partition: {isoSize}");
                        return;
                    }
                }
                else
                {
                    // Open filler data for reading if no seed found
                    FileStream fillerFS = null!;
                    if (File.Exists(fillerPath))
                    {
                        fillerFS = new(fillerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        if (!quiet) Console.WriteLine($"[INFO] Reading random filler data from {fillerPath}");
                    }

                    // Get XGD1 initial seed, if path exists
                    XboxPRNG prng = null!;
                    if (fillerFS == null && xisoType == 0 && File.Exists(seedPath))
                    {
                        FileInfo seedInfo = new(seedPath);
                        if (seedInfo.Length == 4)
                        {
                            using FileStream seedFS = new(seedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                            if (!quiet) Console.WriteLine($"[INFO] Reading initial seed from {seedPath}");
                            prng = new(Utils.ReadUInt(seedFS));
                        }
                    }

                    // Check fillerPath for initial seed
                    if (fillerFS == null && xisoType == 0 && prng == null && File.Exists(fillerPath))
                    {
                        FileInfo seedInfo = new(fillerPath);
                        if (seedInfo.Length == 4)
                        {
                            using FileStream seedFS = new(fillerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                            if (!quiet) Console.WriteLine($"[INFO] Reading initial seed from {seedPath}");
                            prng = new(Utils.ReadUInt(seedFS));
                        }
                    }

                    // Open sectors.txt if an initial seed is being used
                    int[] securitySectors = new int[16];
                    if (fillerFS == null && xisoType == 0 && prng != null)
                    {
                        if (!File.Exists(sectorsTXTPath))
                        {
                            Console.WriteLine("[ERROR] To rebuild from an initial seed, a list of security sector ranges is needed in sectors.txt");
                            return;
                        }
                        if (!quiet) Console.WriteLine($"[INFO] Reading security sector ranges {sectorsTXTPath}");
                        using FileStream sectorsFS = new(sectorsTXTPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        using StreamReader sectorsSR = new StreamReader(sectorsFS);
                        string line;
                        int i = 0;
                        while ((line = sectorsSR.ReadLine()) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line))
                                continue;

                            string[] range = line.Split('-');
                            if (range.Length == 2 && int.TryParse(range[0], out int startSector) && int.TryParse(range[1], out int endSector))
                            {
                                if (startSector < 0 || startSector > (redumpLength / XDVDFS.SECTOR_SIZE - 4096) || endSector - startSector != 4095 || i > 15)
                                {
                                    Console.WriteLine("[ERROR] Invalid security sectors in sectors.txt");
                                    return;
                                }
                                securitySectors[i] = startSector;
                                i += 1;
                            }
                            else
                            {
                                Console.WriteLine("[ERROR] Invalid format of sectors.txt");
                                return;
                            }
                        }
                    }

                    bool writeFiller = fillerFS != null || prng != null;
                    if (!writeFiller && !quiet)
                    {
                        if (xisoType == 0)
                            Console.WriteLine("[INFO] No filler data or seed provided, using XISO only");
                        else
                            Console.WriteLine("[INFO] No filler data provided, using XISO only");
                    }

                    // Parse XISO filesystem for all file extents
                    var validRanges = XDVDFS.GetXISORanges(isoFS, 0, quiet);
                    if (!quiet)
                        foreach (var (start, end) in validRanges.All) Console.WriteLine($"[INFO] XISO File Extent: {start}-{end}");

                    // Write filler data interleaved with XISO
                    long xisoOffsetSector = XISO_OFFSET[xisoType] / XDVDFS.SECTOR_SIZE;
                    long currentByte = 0;
                    isoFS.Seek(0, SeekOrigin.Begin);
                    while (currentByte < xisoLength)
                    {
                        long currentSector = (currentByte + XDVDFS.SECTOR_SIZE - 1) / XDVDFS.SECTOR_SIZE;
                        long xisoBytes = 0;
                        long fillerBytes = 0;

                        // Write zeroes into security sector range (only needed for rebuilding from initial seed)
                        if (prng != null)
                        {
                            bool wipedSectors = false;
                            for (int i = 0; i < securitySectors.Length; i++)
                            {
                                if (currentSector + xisoOffsetSector == securitySectors[i])
                                {
                                    if (!quiet) Console.WriteLine($"[INFO] Wiping security sectors {securitySectors[i]}-{securitySectors[i] + 4095}");
                                    long securitySectorBytes = 4096 * XDVDFS.SECTOR_SIZE;
                                    Utils.WriteZeroes(redumpFS, -1, securitySectorBytes);
                                    prng.SimulateSectors(securitySectorBytes / XDVDFS.SECTOR_SIZE);
                                    currentByte += securitySectorBytes;
                                    isoFS.Seek(securitySectorBytes, SeekOrigin.Current);
                                    wipedSectors = true;
                                    break;
                                }
                            }
                            if (wipedSectors)
                                continue;
                        }

                        // Determine whether current sector is after last file extent
                        if (writeFiller && validRanges.All.Count > 0 && currentSector > validRanges.All[validRanges.All.Count - 1].End)
                        {
                            // Remainder of XISO is filler
                            fillerBytes = xisoLength - currentByte;
                        }
                        else if (writeFiller)
                        {
                            // Determine whether current sector is within a file extent or filler data
                            for (int i = 0; i < validRanges.All.Count; i++)
                            {
                                if (currentSector >= validRanges.All[i].Start && currentSector <= validRanges.All[i].End)
                                {
                                    // Number of bytes remaining in current file extent
                                    xisoBytes = (validRanges.All[i].End + 1) * XDVDFS.SECTOR_SIZE - currentByte;
                                    break;
                                }
                                else if (currentSector < validRanges.All[i].Start && (i == 0 || currentSector > validRanges.All[i - 1].End))
                                {
                                    // Wipe until next file extent
                                    fillerBytes = validRanges.All[i].Start * XDVDFS.SECTOR_SIZE - currentByte;
                                    break;
                                }
                            }
                        }

                        // If rebuilding from initial seed, trim bytes to read/write until next security sector
                        if (prng != null)
                        {
                            for (int i = 0; i < securitySectors.Length; i++)
                            {
                                if (currentSector + xisoOffsetSector < securitySectors[i] + 4095)
                                {
                                    if (currentSector + xisoOffsetSector + fillerBytes / XDVDFS.SECTOR_SIZE >= securitySectors[i])
                                    {
                                        fillerBytes = (securitySectors[i] - currentSector - xisoOffsetSector) * XDVDFS.SECTOR_SIZE;
                                        break;
                                    }
                                    else if (currentSector + xisoOffsetSector + xisoBytes / XDVDFS.SECTOR_SIZE >= securitySectors[i])
                                    {
                                        xisoBytes = (securitySectors[i] - currentSector - xisoOffsetSector) * XDVDFS.SECTOR_SIZE;
                                        break;
                                    }
                                }
                            }
                        }

                        if (fillerBytes > 0)
                        {
                            // Validity check
                            if (fillerBytes % XDVDFS.SECTOR_SIZE != 0)
                            {
                                Console.WriteLine("[ERROR] Unexpected Error 6, please report this.");
                                return;
                            }
                            // Write filler data
                            if (prng != null)
                            {
                                // Generate filler data
                                prng.WriteSectors(redumpFS, fillerBytes / XDVDFS.SECTOR_SIZE);
                            }
                            else if (!Utils.WriteBytes(fillerFS, redumpFS, -1, fillerBytes))
                            {
                                Console.WriteLine($"[ERROR] Failed writing random filler data.");
                                return;
                            }
                            currentByte += fillerBytes;
                            isoFS.Seek(fillerBytes, SeekOrigin.Current);
                        }
                        else
                        {
                            // Write data to XISO
                            long bytesToWrite;
                            if (xisoBytes > 0)
                                bytesToWrite = xisoBytes;
                            else
                                bytesToWrite = xisoLength - currentByte;
                            if (!Utils.WriteBytes(isoFS, redumpFS, -1, bytesToWrite))
                            {
                                Console.WriteLine($"[ERROR] Failed writing game partition (XISO).");
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
                        Console.WriteLine("[ERROR] Unexpected Error 7, please report this.");
                        return;
                    }
                }

                // Write layer 1 padding
                long l1Length = VIDEO_L1_LENGTH[videoType];
                long l1Padding = (redumpLength - l1Length) - (xisoOffset + xisoLength);
                Utils.WriteZeroes(redumpFS, -1, l1Padding);

                // If writing system update file, stop video partition early
                long suSize = 0;
                if (File.Exists(updatePath))
                {
                    if (!quiet) Console.WriteLine($"[INFO] Rebuilding with update file: {updatePath}");
                    FileInfo suInfo = new(updatePath);
                    suSize = suInfo.Length;
                    l1Length -= suSize + XDVDFS.SECTOR_SIZE;
                }

                // Write layer 1 portion of video partition
                if (!Utils.WriteBytes(videoFS, redumpFS, l0Length, l1Length))
                {
                    Console.WriteLine($"[ERROR] Failed writing layer 1 portion of video partition.");
                    return;
                }

                // Write system update file
                if (File.Exists(updatePath))
                {
                    // Open system update file for reading
                    using FileStream updateFS = new(updatePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (!quiet) Console.WriteLine($"[INFO] Reading system update from {updatePath}");

                    // Write system update file to redump ISO
                    if (!Utils.WriteBytes(updateFS, redumpFS, 0, suSize))
                    {
                        Console.WriteLine($"[ERROR] Failed writing system update file.");
                        return;
                    }

                    // Write final video partition sector
                    videoFS.Seek(-XDVDFS.SECTOR_SIZE, SeekOrigin.End);
                    if (!Utils.WriteBytes(videoFS, redumpFS, -1, XDVDFS.SECTOR_SIZE))
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
