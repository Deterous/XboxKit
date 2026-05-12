using System;
using System.Collections.Generic;
using System.IO;
using LibXGD;

namespace XboxKit
{
    internal class Program
    {
        static void PrintHelp()
        {
            // Print help text to console
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

        // Returns true if OK to proceed, false if user declined
        static bool ConfirmOverwrite(string path, bool assumeNo)
        {
            if (!File.Exists(path))
                return true;
            if (assumeNo)
            {
                Console.WriteLine($"[ERROR] File already exists: {path}");
                return false;
            }
            Console.WriteLine($"[WARNING] File already exists: {path}");
            Console.WriteLine($"Would you like to overwrite? (Y/N)");
            string? response = Console.ReadLine()?.ToUpper();
            return response == "Y" || response == "YES";
        }

        static void Main(string[] args)
        {
            #region Initial Setup

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
                            case 'm':
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

            string isoPath = filePaths[0];
            if (string.IsNullOrEmpty(isoPath) || !File.Exists(isoPath))
            {
                Console.WriteLine($"[ERROR] Invalid file path: {isoPath}");
                return;
            }

            string dir = Path.GetDirectoryName(isoPath) ?? "";
            string filename = Path.GetFileNameWithoutExtension(isoPath) ?? "";
            string extension = Path.GetExtension(isoPath);

            // Strip compound extensions
            string[] compoundExtensions = [".video.iso", ".redump.iso", ".skeleton.xiso"];
            foreach (var ext in compoundExtensions)
            {
                if (isoPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    filename = Path.GetFileName(isoPath).Substring(0, Path.GetFileName(isoPath).Length - ext.Length);
                    break;
                }
            }

            // TODO: Add SabreTools.Serialization
            if (extractZAR || extension == ".zar")
            {
                Console.WriteLine("ZArchive support is coming soon!");
                return;
            }

            string isoBasePath = Path.Combine(dir, $"{filename}.iso");
            string redumpPath = (isoBasePath == isoPath || File.Exists(isoBasePath)) ? Path.Combine(dir, $"{filename}.redump.iso") : isoBasePath;
            string xrdPath = Path.Combine(dir, $"{filename}.xrd");
            string outputPath = Path.Combine(dir, $"{filename}");
            string skeletonPath = Path.Combine(dir, $"{filename}.skeleton.xiso");
            string fillerPath = Path.Combine(dir, $"{filename}.filler");
            string seedPath = Path.Combine(dir, $"{filename}.seed");
            string sectorsTXTPath = Path.Combine(dir, "sectors.txt");
            string updatePath = Path.Combine(dir, "su20076000_00000000");
            string videoPath = Path.Combine(dir, $"{filename}.video.iso");
            string xisoPath = Path.Combine(dir, $"{filename}.xiso");
            string zarPath = Path.Combine(dir, $"{filename}.zar");

            // Detect additional input files by size/extension
            for (int f = 1; f < filePaths.Count; f++)
            {
                string fp = filePaths[f];
                if (!File.Exists(fp))
                {
                    Console.WriteLine($"[ERROR] Invalid file path: {fp}");
                    return;
                }
                long fpSize = new FileInfo(fp).Length;
                if (Array.IndexOf(XGD.VIDEO_LENGTH, fpSize) >= 0 || fp.EndsWith(".video.iso", StringComparison.OrdinalIgnoreCase))
                    videoPath = fp;
                else if (fp.EndsWith(".seed", StringComparison.OrdinalIgnoreCase) || fpSize == 4)
                    seedPath = fp;
                else if (Path.GetFileName(fp).StartsWith("su200760", StringComparison.OrdinalIgnoreCase))
                    updatePath = fp;
                else
                    fillerPath = fp;
            }

            // Resolve output path conflicts with input file
            string isoFullPath = Path.GetFullPath(isoPath);
            if (Path.GetFullPath(redumpPath) == isoFullPath)
                redumpPath = Path.Combine(dir, $"{filename}.redump.iso");
            if (Path.GetFullPath(xisoPath) == isoFullPath)
                xisoPath = Path.Combine(dir, $"{filename}.wiped.xiso");
            if (Path.GetFullPath(skeletonPath) == isoFullPath)
                skeletonPath = Path.Combine(dir, $"{filename}.out.skeleton.xiso");
            if (Path.GetFullPath(videoPath) == isoFullPath)
                videoPath = Path.Combine(dir, $"{filename}.out.video.iso");
            if (Path.GetFullPath(fillerPath) == isoFullPath)
                fillerPath = Path.Combine(dir, $"{filename}.out.filler");
            if (Path.GetFullPath(seedPath) == isoFullPath)
                seedPath = Path.Combine(dir, $"{filename}.out.seed");

            // Compare input ISO file size to determine file type
            FileInfo isoInfo = new(isoPath);
            long isoSize = isoInfo.Length;
            int redumpIsoType = Array.IndexOf(XGD.REDUMP_ISO_LENGTH, isoSize);
            int videoIsoType = Array.IndexOf(XGD.VIDEO_LENGTH, isoSize);
            int xisoType = Array.IndexOf(XGD.XISO_LENGTH, isoSize);

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
                    string? response = Console.ReadLine()?.ToUpper();
                    if (response != "Y" && response != "YES")
                        return;
                    extractVideo = true;
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
                    Console.WriteLine("       For now, use -w with -r");
                    if (assumeNo)
                        return;
                }

                // Check that files don't already exist
                if (!assumeYes && extractXISO && !ConfirmOverwrite(xisoPath, assumeNo))
                    return;
                if (!assumeYes && extractVideo && !ConfirmOverwrite(videoPath, assumeNo))
                    return;
                if (!assumeYes && extractFiller && !ConfirmOverwrite(fillerPath, assumeNo))
                    return;
                if (!assumeYes && extractUpdate && !ConfirmOverwrite(updatePath, assumeNo))
                    return;
                if (!assumeYes && extractSeed && !ConfirmOverwrite(seedPath, assumeNo))
                    return;

                #endregion

                // Determine disc layout type
                int xgdType = XGD.GetXGDType(redumpIsoType);

                // Open redump ISO for reading
                if (!quiet) Console.WriteLine($"[INFO] Reading redump ISO from {isoPath}");
                using FileStream isoFS = new(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);

                if (outputFiles || extractXRD)
                {
                    var wrapper = SabreTools.Wrappers.XboxISO.Create(isoFS);
                    if (wrapper is null)
                    {
                        Console.WriteLine($"[ERROR] Invalid ISO");
                        return;
                    }

                    // Extract game files
                    if (outputFiles)
                    {
                        if (!quiet) Console.WriteLine($"[INFO] Parsing Xbox DVD filesystem");
                        isoFS.Seek(XGD.XISO_OFFSET[xgdType], SeekOrigin.Begin);

                        if (!Directory.Exists(outputPath))
                            Directory.CreateDirectory(outputPath);

                        if (!quiet) Console.WriteLine($"[INFO] Outputting game files to {outputPath}");
                        if (!wrapper.ExtractGamePartition(outputPath, !quiet))
                        {
                            Console.WriteLine($"[ERROR] Failed to extract files from {isoPath}");
                            return;
                        }
                    }

                    // Extract rebuild data
                    if (extractXRD)
                    {
                        // Create file for XRD
                        if (!quiet) Console.WriteLine($"[INFO] Writing XRD metadata file to {xrdPath}");
                        var xrd = XRD.GetXRD(isoFS, wrapper, redumpIsoType);
                        if (xrd is null)
                        {
                            Console.WriteLine($"[ERROR] Failed to create XRD");
                            return;
                        }
                        var writer = new SabreTools.Serialization.Writers.XRD();
                        if(!writer.SerializeFile(xrd, xrdPath))
                        {
                            Console.WriteLine($"[ERROR] Failed to write XRD");
                            return;
                        }
                    }
                }

                // Extract video partition
                if (extractVideo)
                {
                    if (!quiet) Console.WriteLine($"[INFO] Writing video partition to {videoPath}");
                    if (!XGD.ExtractVideo(isoFS, videoPath, redumpIsoType))
                    {
                        Console.WriteLine($"[ERROR] Failed writing video partition.");
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
                    uint? seed = XboxPRNG.ExtractSeed(isoFS, XGD.XISO_OFFSET[xgdType], quiet);
                    if (seed.HasValue)
                    {
                        if (!quiet) Console.WriteLine($"[INFO] Filler data seed: {seed.Value:X8}");
                        using FileStream seedFS = new(seedPath, FileMode.Create, FileAccess.Write, FileShare.None);
                        byte[] seedBytes = BitConverter.GetBytes(seed.Value);
                        seedFS.Write(seedBytes, 0, seedBytes.Length);
                        if (!quiet) Console.WriteLine($"[INFO] Writing filler data to {seedPath}");
                    }
                    else
                    {
                        Console.WriteLine("[ERROR] Failed to extract XGD1 seed.");
                    }
                }
                else if (extractSeed)
                {
                    if (!quiet) Console.WriteLine($"[INFO] Cannot extract seed from Xbox 360 discs");
                }

                // Quit early if we're not extracting data from game partition
                if (!extractXISO && !extractFiller && !extractSkeleton)
                    return;

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

                // Process XISO
                if (!XDVDFS.ProcessXISO(isoFS, XGD.XISO_OFFSET[xgdType], XGD.XISO_LENGTH[xgdType], xisoFS, fillerFS, wipeXISO, trimXISO, extractSkeleton, quiet))
                {
                    Console.WriteLine("[ERROR] Failed processing XISO.");
                    return;
                }

                // Close files
                if (xisoFS != null)
                    xisoFS.Dispose();
                if (fillerFS != null)
                    fillerFS.Dispose();

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
                        Console.WriteLine("[INFO] Cannot extract filler (-r), input file is video ISO.");
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
                if (!assumeYes && extractUpdate && !ConfirmOverwrite(updatePath, assumeNo))
                    return;

                // Check that video partition is from XGD3 disc
                if (videoIsoType != 16 && videoIsoType != 17 && videoIsoType != 18)
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
                        Console.WriteLine("[INFO] Cannot extract XISO (-x), input file is already XISO (or unexpected ISO).");
                        invalidOptions = true;
                    }
                    if (extractVideo)
                    {
                        Console.WriteLine("[INFO] Cannot extract video (-v), input file is XISO (or unexpected ISO).");
                        invalidOptions = true;
                    }
                    if (extractUpdate)
                    {
                        Console.WriteLine("[INFO] Cannot extract update (-u), input file is XISO (or unexpected ISO).");
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
                    {
                        if (wipeXISO && !quiet)
                            Console.WriteLine($"[INFO] Writing wiped XISO to {xisoPath}");
                        else if (trimXISO && !quiet)
                            Console.WriteLine($"[INFO] Writing XISO to {xisoPath}");
                        xisoFS = new FileStream(xisoPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    }

                    // Create file for filler data
                    FileStream fillerFS = null!;
                    if (extractFiller)
                    {
                        if (!quiet) Console.WriteLine($"[INFO] Extracting filler data to {fillerPath}");
                        fillerFS = new FileStream(fillerPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    }

                    // Process XISO
                    if (!XDVDFS.ProcessXISO(isoFS, 0, isoSize, xisoFS, fillerFS, wipeXISO, trimXISO, extractSkeleton, quiet))
                    {
                        Console.WriteLine("[ERROR] Failed processing XISO.");
                        return;
                    }

                    // Close files
                    if (xisoFS != null)
                        xisoFS.Dispose();
                    if (fillerFS != null)
                        fillerFS.Dispose();

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
                int videoType = Array.IndexOf(XGD.VIDEO_LENGTH, videoSize);
                if (videoType < 0)
                {
                    Console.WriteLine("[ERROR] Unexpected video partition ISO size. Your video file may be trimmed or corrupt.");
                    return;
                }

                // Determine intended xisoType based on video ISO length
                int rebuildXisoType = XGD.GetXISOTypeFromVideo(videoType);

                // Create redump ISO
                using FileStream redumpFS = new(redumpPath, FileMode.Create, FileAccess.Write, FileShare.None);
                if (!quiet) Console.WriteLine($"[INFO] Writing redump ISO to {redumpPath}");

                // Open video ISO for reading
                using FileStream videoFS = new(videoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!quiet) Console.WriteLine($"[INFO] Reading video partition from {videoPath}");

                // Open filler data for reading if available
                FileStream fillerFS = null!;
                if (File.Exists(fillerPath))
                {
                    fillerFS = new(fillerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (!quiet) Console.WriteLine($"[INFO] Reading random filler data from {fillerPath}");
                }

                // Get XGD1 initial seed, if path exists
                XboxPRNG prng = null!;
                if (fillerFS == null && rebuildXisoType == 0 && File.Exists(seedPath))
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
                if (fillerFS == null && rebuildXisoType == 0 && prng == null && File.Exists(fillerPath))
                {
                    FileInfo seedInfo = new(fillerPath);
                    if (seedInfo.Length == 4)
                    {
                        using FileStream seedFS = new(fillerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        if (!quiet) Console.WriteLine($"[INFO] Reading initial seed from {fillerPath}");
                        prng = new(Utils.ReadUInt(seedFS));
                    }
                }

                // Open sectors.txt if an initial seed is being used
                int[] securitySectors = new int[16];
                if (fillerFS == null && rebuildXisoType == 0 && prng != null)
                {
                    long redumpLength = XGD.GetRedumpLength(videoType);
                    if (!File.Exists(sectorsTXTPath))
                    {
                        Console.WriteLine("[ERROR] To rebuild from an initial seed, a list of security sector ranges is needed in sectors.txt");
                        return;
                    }
                    if (!quiet) Console.WriteLine($"[INFO] Reading security sector ranges {sectorsTXTPath}");
                    using FileStream sectorsFS = new(sectorsTXTPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using StreamReader sectorsSR = new StreamReader(sectorsFS);
                    string? line;
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

                // Rebuild redump ISO
                if (!XGD.RebuildRedump(isoFS, redumpFS, videoFS, fillerFS, prng, securitySectors, videoType, quiet))
                {
                    Console.WriteLine("[ERROR] Failed rebuilding redump ISO.");
                    return;
                }

                // Close filler file
                if (fillerFS != null)
                    fillerFS.Dispose();

                // Insert system update file if available
                if (!XGD.RebuildWithUpdate(redumpFS, videoFS, updatePath, videoType, quiet))
                {
                    Console.WriteLine("[ERROR] Failed writing system update file.");
                    return;
                }

                #endregion
            }
        }
    }
}
