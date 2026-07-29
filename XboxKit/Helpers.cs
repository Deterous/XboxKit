using System;
using System.Collections.Generic;
using System.IO;

namespace XboxKit
{
    internal static class Helpers
    {
        internal static void PrintHelp()
        {
            // Print help text to console
            Console.WriteLine("XboxKit (c) Deterous 2024-2026");
            Console.WriteLine("");
            Console.WriteLine("Rebuild mode: Don't use any options (combines input files)");
            Console.WriteLine("Usage: xboxkit.exe <input.xiso> [files...]");
            Console.WriteLine("");
            Console.WriteLine("Extract mode: Use one or more options (splits input file)");
            Console.WriteLine("Usage: xboxkit.exe [options] <input.iso>");
            Console.WriteLine("");
            Console.WriteLine("Batch options (for redump ISO):");
            Console.WriteLine("  -a, --all       All options for lossless XISO extraction (-rstuvwx)");
            Console.WriteLine("  -b, --best      Create trimmed/wiped XISO only (-twx)");
            Console.WriteLine("  -c, --compress  Options for lossless ZArchive compression (-puvz)");
            Console.WriteLine("");
            Console.WriteLine("Manual options:");
            // Console.WriteLine("  -m, --metadata  Extract metadata in the form of an XRD file");
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

        internal static bool ConfirmOverwrite(string path, bool assumeNo)
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

        internal static Options? ParseArgs(string[] args)
        {
            Options opts = new();
            List<string> filePaths = new();

            if (args.Length == 0)
            {
                PrintHelp();
                return null;
            }
            bool endOfOptions = false;
            opts.OptionsProvided = false;
            foreach (var arg in args)
            {
                if (endOfOptions)
                {
                    filePaths.Add(arg);
                    continue;
                }
                if (arg == "--")
                {
                    endOfOptions = true;
                    continue;
                }
                if (arg.StartsWith("--"))
                {
                    switch (arg.ToLowerInvariant())
                    {
                        case "--help":
                            opts.Help = true;
                            break;
                        case "--all":
                            opts.OptionsProvided = true;
                            opts.ExtractFiller = true;
                            opts.ExtractSeed = true;
                            opts.TrimXISO = true;
                            opts.ExtractUpdate = true;
                            opts.ExtractVideo = true;
                            opts.WipeXISO = true;
                            opts.ExtractXISO = true;
                            break;
                        case "--best":
                            opts.OptionsProvided = true;
                            opts.TrimXISO = true;
                            opts.WipeXISO = true;
                            opts.ExtractXISO = true;
                            break;
                        case "--compress":
                            opts.OptionsProvided = true;
                            opts.ExtractSkeleton = true;
                            opts.ExtractUpdate = true;
                            opts.ExtractVideo = true;
                            opts.ExtractZAR = true;
                            break;
                        case "--metadata":
                            opts.OptionsProvided = true;
                            opts.ExtractXRD = true;
                            break;
                        case "--no":
                            opts.AssumeNo = true;
                            break;
                        case "--output":
                            opts.OptionsProvided = true;
                            opts.OutputFiles = true;
                            break;
                        case "--petrify":
                            opts.OptionsProvided = true;
                            opts.ExtractSkeleton = true;
                            break;
                        case "--quiet":
                            opts.Quiet = true;
                            break;
                        case "--random":
                            opts.OptionsProvided = true;
                            opts.ExtractFiller = true;
                            break;
                        case "--seed":
                            opts.OptionsProvided = true;
                            opts.ExtractSeed = true;
                            break;
                        case "--trim":
                            opts.OptionsProvided = true;
                            opts.TrimXISO = true;
                            break;
                        case "--update":
                            opts.OptionsProvided = true;
                            opts.ExtractUpdate = true;
                            break;
                        case "--video":
                            opts.OptionsProvided = true;
                            opts.ExtractVideo = true;
                            break;
                        case "--wipe":
                            opts.OptionsProvided = true;
                            opts.WipeXISO = true;
                            break;
                        case "--xiso":
                            opts.OptionsProvided = true;
                            opts.ExtractXISO = true;
                            break;
                        case "--yes":
                            opts.AssumeYes = true;
                            break;
                        case "--zar":
                            opts.OptionsProvided = true;
                            opts.ExtractZAR = true;
                            break;
                        default:
                            Console.WriteLine($"[ERROR] Unknown option: {arg}");
                            PrintHelp();
                            return null;
                    }
                }
                else if (arg.StartsWith("-"))
                {
                    string flags = arg.Substring(1).ToLowerInvariant();
                    if (flags.EndsWith('-'))
                    {
                        endOfOptions = true;
                        flags = flags.Substring(0, flags.Length - 1);
                    }
                    else if (flags.Contains('-'))
                    {
                        Console.WriteLine($"[ERROR] Invalid flag format: {arg}");
                        PrintHelp();
                        return null;
                    }
                    foreach (char flag in flags)
                    {
                        switch (flag)
                        {
                            case 'h':
                                opts.Help = true;
                                break;
                            case 'a':
                                opts.OptionsProvided = true;
                                opts.ExtractFiller = true;
                                opts.ExtractSeed = true;
                                opts.TrimXISO = true;
                                opts.ExtractUpdate = true;
                                opts.ExtractVideo = true;
                                opts.WipeXISO = true;
                                opts.ExtractXISO = true;
                                break;
                            case 'b':
                                opts.OptionsProvided = true;
                                opts.TrimXISO = true;
                                opts.WipeXISO = true;
                                opts.ExtractXISO = true;
                                break;
                            case 'c':
                                opts.OptionsProvided = true;
                                opts.ExtractSkeleton = true;
                                opts.ExtractUpdate = true;
                                opts.ExtractVideo = true;
                                opts.ExtractZAR = true;
                                break;
                            case 'm':
                                opts.OptionsProvided = true;
                                opts.ExtractXRD = true;
                                break;
                            case 'n':
                                opts.AssumeNo = true;
                                break;
                            case 'o':
                                opts.OptionsProvided = true;
                                opts.OutputFiles = true;
                                break;
                            case 'p':
                                opts.OptionsProvided = true;
                                opts.ExtractSkeleton = true;
                                break;
                            case 'q':
                                opts.Quiet = true;
                                break;
                            case 'r':
                                opts.OptionsProvided = true;
                                opts.ExtractFiller = true;
                                break;
                            case 's':
                                opts.OptionsProvided = true;
                                opts.ExtractSeed = true;
                                break;
                            case 't':
                                opts.OptionsProvided = true;
                                opts.TrimXISO = true;
                                break;
                            case 'u':
                                opts.OptionsProvided = true;
                                opts.ExtractUpdate = true;
                                break;
                            case 'v':
                                opts.OptionsProvided = true;
                                opts.ExtractVideo = true;
                                break;
                            case 'w':
                                opts.OptionsProvided = true;
                                opts.WipeXISO = true;
                                break;
                            case 'x':
                                opts.OptionsProvided = true;
                                opts.ExtractXISO = true;
                                break;
                            case 'y':
                                opts.AssumeYes = true;
                                break;
                            case 'z':
                                opts.OptionsProvided = true;
                                opts.ExtractZAR = true;
                                break;
                            default:
                                Console.WriteLine($"[ERROR] Unknown flag: -{flag}");
                                PrintHelp();
                                return null;
                        }
                    }
                }
                else
                {
                    filePaths.Add(arg);
                }
            }
            if (opts.Help || filePaths.Count == 0)
            {
                PrintHelp();
                return null;
            }
            if (opts.AssumeNo && opts.AssumeYes)
            {
                Console.WriteLine("[ERROR] Cannot use both --no (-n) and --yes (-y)");
                return null;
            }

            if (filePaths.Count > 1 && opts.OptionsProvided)
            {
                Console.WriteLine("[ERROR] Extract mode only accepts one input file");
                return null;
            }

            if (!ResolvePaths(opts, filePaths))
                return null;

            return opts;
        }

        /// Determines all output paths on the Options object based on the input file
        static bool ResolvePaths(Options opts, List<string> filePaths)
        {
            opts.IsoPath = filePaths[0];
            if (string.IsNullOrEmpty(opts.IsoPath) || !File.Exists(opts.IsoPath))
            {
                Console.WriteLine($"[ERROR] Invalid file path: {opts.IsoPath}");
                return false;
            }

            string dir = Path.GetDirectoryName(opts.IsoPath) ?? "";
            string filename = Path.GetFileNameWithoutExtension(opts.IsoPath) ?? "";

            // Strip compound extensions
            string[] compoundExtensions = [".video.iso", ".redump.iso", ".skeleton.xiso"];
            foreach (var ext in compoundExtensions)
            {
                if (opts.IsoPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    filename = Path.GetFileName(opts.IsoPath).Substring(0, Path.GetFileName(opts.IsoPath).Length - ext.Length);
                    break;
                }
            }

            // TODO: Add SabreTools.Serialization for ZAR rebuild
            if (Path.GetExtension(opts.IsoPath).Equals(".zar", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("ZArchive rebuild is coming soon!");
                return false;
            }

            // TODO: Implement XRD metadata extraction
            if (opts.ExtractXRD)
            {
                Console.WriteLine("XRD metadata extraction is coming soon!");
                return false;
            }

            // Detect additional input files by extension and size
            for (int i = 1; i < filePaths.Count; i++)
            {
                string filePath = filePaths[i];
                if (Directory.Exists(filePath))
                {
                    if (!string.IsNullOrEmpty(opts.OutputPath))
                    {
                        Console.WriteLine("[ERROR] Provide only one output directory");
                        return false;
                    }
                    opts.OutputPath = filePath;
                    continue;
                }
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"[ERROR] File not found: {filePath}");
                    return false;
                }
                long fileSize = new FileInfo(filePath).Length;
                string fileName = Path.GetFileName(filePath);

                if (string.IsNullOrEmpty(opts.VideoPath) && (filePath.EndsWith(".video.iso", StringComparison.OrdinalIgnoreCase)
                    || Array.IndexOf(LibXGD.XGD.VIDEO_LENGTH, fileSize) >= 0))
                    opts.VideoPath = filePath;
                else if (string.IsNullOrEmpty(opts.SkeletonPath) && (filePath.EndsWith(".skeleton.xiso", StringComparison.OrdinalIgnoreCase)
                    || filePath.EndsWith(".skeleton", StringComparison.OrdinalIgnoreCase)))
                    opts.SkeletonPath = filePath;
                else if (string.IsNullOrEmpty(opts.XisoPath) && (filePath.EndsWith(".xiso", StringComparison.OrdinalIgnoreCase)
                    || Array.IndexOf(LibXGD.XGD.XISO_LENGTH, fileSize) >= 0))
                    opts.XisoPath = filePath;
                else if (string.IsNullOrEmpty(opts.SeedPath) && (filePath.EndsWith(".seed", StringComparison.OrdinalIgnoreCase) || fileSize == 4))
                    opts.SeedPath = filePath;
                else if (string.IsNullOrEmpty(opts.UpdatePath) && fileName.StartsWith("su20076000_00000000", StringComparison.OrdinalIgnoreCase))
                    opts.UpdatePath = filePath;
                else if (string.IsNullOrEmpty(opts.ZarPath) && filePath.EndsWith(".zar", StringComparison.OrdinalIgnoreCase))
                    opts.ZarPath = filePath;
                else if (string.IsNullOrEmpty(opts.FillerPath) && (filePath.EndsWith(".filler", StringComparison.OrdinalIgnoreCase)
                    || filePath.EndsWith(".rc4", StringComparison.OrdinalIgnoreCase)))
                    opts.FillerPath = filePath;
                else if (string.IsNullOrEmpty(opts.HashPath) && filePath.EndsWith(".hash", StringComparison.OrdinalIgnoreCase))
                    opts.HashPath = filePath;
                else if (string.IsNullOrEmpty(opts.XrdPath) && filePath.EndsWith(".xrd", StringComparison.OrdinalIgnoreCase))
                    opts.XrdPath = filePath;
                else if (string.IsNullOrEmpty(opts.SectorsTXTPath) && fileName.Equals("sectors.txt", StringComparison.OrdinalIgnoreCase))
                    opts.SectorsTXTPath = filePath;
                else
                {
                    Console.WriteLine($"[ERROR] Invalid input file: {filePath}");
                    return false;
                }
            }

            // Set default paths for any that weren't explicitly provided
            string isoBasePath = Path.Combine(dir, $"{filename}.iso");
            if (string.IsNullOrEmpty(opts.RedumpPath)) opts.RedumpPath = (isoBasePath == opts.IsoPath || File.Exists(isoBasePath)) ? Path.Combine(dir, $"{filename}.redump.iso") : isoBasePath;
            if (string.IsNullOrEmpty(opts.XrdPath)) opts.XrdPath = Path.Combine(dir, $"{filename}.xrd");
            if (string.IsNullOrEmpty(opts.OutputPath)) opts.OutputPath = Path.Combine(dir, $"{filename}");
            if (string.IsNullOrEmpty(opts.SkeletonPath)) opts.SkeletonPath = Path.Combine(dir, $"{filename}.skeleton.xiso");
            if (string.IsNullOrEmpty(opts.HashPath)) opts.HashPath = Path.Combine(dir, $"{filename}.hash");
            if (string.IsNullOrEmpty(opts.FillerPath)) opts.FillerPath = Path.Combine(dir, $"{filename}.filler");
            if (string.IsNullOrEmpty(opts.SeedPath)) opts.SeedPath = Path.Combine(dir, $"{filename}.seed");
            if (string.IsNullOrEmpty(opts.SectorsTXTPath)) opts.SectorsTXTPath = Path.Combine(dir, "sectors.txt");
            if (string.IsNullOrEmpty(opts.UpdatePath)) opts.UpdatePath = Path.Combine(dir, "su20076000_00000000");
            if (string.IsNullOrEmpty(opts.VideoPath)) opts.VideoPath = Path.Combine(dir, $"{filename}.video.iso");
            if (string.IsNullOrEmpty(opts.XisoPath)) opts.XisoPath = Path.Combine(dir, $"{filename}.xiso");
            if (string.IsNullOrEmpty(opts.ZarPath)) opts.ZarPath = Path.Combine(dir, $"{filename}.zar");

            // Resolve output path conflicts with input file
            string isoFullPath = Path.GetFullPath(opts.IsoPath);
            if (Path.GetFullPath(opts.RedumpPath) == isoFullPath)
                opts.RedumpPath = Path.Combine(dir, $"{filename}.redump.iso");
            if (Path.GetFullPath(opts.XisoPath) == isoFullPath)
                opts.XisoPath = Path.Combine(dir, $"{filename}.wiped.xiso");
            if (Path.GetFullPath(opts.SkeletonPath) == isoFullPath)
                opts.SkeletonPath = Path.Combine(dir, $"{filename}.out.skeleton.xiso");
            if (Path.GetFullPath(opts.VideoPath) == isoFullPath)
                opts.VideoPath = Path.Combine(dir, $"{filename}.out.video.iso");
            if (Path.GetFullPath(opts.FillerPath) == isoFullPath)
                opts.FillerPath = Path.Combine(dir, $"{filename}.out.filler");
            if (Path.GetFullPath(opts.SeedPath) == isoFullPath)
                opts.SeedPath = Path.Combine(dir, $"{filename}.out.seed");

            // Compare input ISO file size to determine file type
            FileInfo isoInfo = new(opts.IsoPath);
            opts.IsoSize = isoInfo.Length;
            opts.RedumpIsoType = Array.IndexOf(LibXGD.XGD.REDUMP_ISO_LENGTH, opts.IsoSize);
            opts.VideoIsoType = Array.IndexOf(LibXGD.XGD.VIDEO_LENGTH, opts.IsoSize);
            opts.XisoType = Array.IndexOf(LibXGD.XGD.XISO_LENGTH, opts.IsoSize);

            // Determine video type from video partition file
            if (File.Exists(opts.VideoPath))
                opts.VideoType = Array.IndexOf(LibXGD.XGD.VIDEO_LENGTH, new FileInfo(opts.VideoPath).Length);

            // Determine mode and XGD type
            if (opts.RedumpIsoType >= 0)
            {
                opts.Mode = Mode.ExtractRedump;
                opts.XGDType = LibXGD.XGD.GetXGDType(opts.RedumpIsoType);
            }
            else if (opts.VideoIsoType >= 0)
            {
                opts.Mode = Mode.ExtractVideo;
            }
            else if (opts.OptionsProvided)
            {
                opts.Mode = Mode.ProcessXISO;
                if (opts.XisoType >= 0)
                    opts.XGDType = opts.XisoType;
            }
            else
            {
                opts.Mode = Mode.RebuildISO;
                if (opts.VideoType >= 0)
                    opts.XGDType = LibXGD.XGD.GetXISOTypeFromVideo(opts.VideoType);
            }

            return true;
        }
    }
}
