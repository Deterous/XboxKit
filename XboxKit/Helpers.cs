using System;
using System.IO;
using System.Linq;

namespace XboxKit
{
    internal static class Helpers
    {
        internal static void PrintHelp()
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

            if (args.Length == 0)
            {
                PrintHelp();
                return null;
            }
            foreach (var arg in args)
            {
                if (arg.StartsWith("--"))
                {
                    switch (arg.ToLowerInvariant())
                    {
                        case "--help":
                            opts.Help = true;
                            break;
                        case "--quiet":
                            opts.Quiet = true;
                            break;
                        case "--all":
                            opts.ExtractFiller = true;
                            opts.ExtractSeed = true;
                            opts.TrimXISO = true;
                            opts.ExtractUpdate = true;
                            opts.ExtractVideo = true;
                            opts.WipeXISO = true;
                            opts.ExtractXISO = true;
                            break;
                        case "--best":
                            opts.TrimXISO = true;
                            opts.WipeXISO = true;
                            opts.ExtractXISO = true;
                            break;
                        case "--compress":
                            opts.ExtractSkeleton = true;
                            opts.ExtractUpdate = true;
                            opts.ExtractVideo = true;
                            opts.ExtractZAR = true;
                            break;
                        case "--metadata":
                            opts.ExtractXRD = true;
                            break;
                        case "--no":
                            opts.AssumeNo = true;
                            break;
                        case "--output":
                            opts.OutputFiles = true;
                            break;
                        case "--petrify":
                            opts.ExtractSkeleton = true;
                            break;
                        case "--random":
                            opts.ExtractFiller = true;
                            break;
                        case "--seed":
                            opts.ExtractSeed = true;
                            break;
                        case "--trim":
                            opts.TrimXISO = true;
                            break;
                        case "--update":
                            opts.ExtractUpdate = true;
                            break;
                        case "--video":
                            opts.ExtractVideo = true;
                            break;
                        case "--wipe":
                            opts.WipeXISO = true;
                            break;
                        case "--xiso":
                            opts.ExtractXISO = true;
                            break;
                        case "--yes":
                            opts.AssumeYes = true;
                            break;
                        case "--zar":
                            opts.ExtractZAR = true;
                            break;
                        default:
                            opts.FilePaths.Add(arg);
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
                                opts.Help = true;
                                break;
                            case 'q':
                                opts.Quiet = true;
                                break;
                            case 'a':
                                opts.ExtractFiller = true;
                                opts.ExtractSeed = true;
                                opts.TrimXISO = true;
                                opts.ExtractUpdate = true;
                                opts.ExtractVideo = true;
                                opts.WipeXISO = true;
                                opts.ExtractXISO = true;
                                break;
                            case 'b':
                                opts.TrimXISO = true;
                                opts.WipeXISO = true;
                                opts.ExtractXISO = true;
                                break;
                            case 'c':
                                opts.ExtractSkeleton = true;
                                opts.ExtractUpdate = true;
                                opts.ExtractVideo = true;
                                opts.ExtractZAR = true;
                                break;
                            case 'm':
                                opts.ExtractXRD = true;
                                break;
                            case 'n':
                                opts.AssumeNo = true;
                                break;
                            case 'o':
                                opts.OutputFiles = true;
                                break;
                            case 'p':
                                opts.ExtractSkeleton = true;
                                break;
                            case 'r':
                                opts.ExtractFiller = true;
                                break;
                            case 's':
                                opts.ExtractSeed = true;
                                break;
                            case 't':
                                opts.TrimXISO = true;
                                break;
                            case 'u':
                                opts.ExtractUpdate = true;
                                break;
                            case 'v':
                                opts.ExtractVideo = true;
                                break;
                            case 'w':
                                opts.WipeXISO = true;
                                break;
                            case 'x':
                                opts.ExtractXISO = true;
                                break;
                            case 'y':
                                opts.AssumeYes = true;
                                break;
                            case 'z':
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
                    opts.FilePaths.Add(arg);
                }
            }
            if (opts.Help || opts.FilePaths.Count == 0)
            {
                PrintHelp();
                return null;
            }
            if (opts.AssumeNo && opts.AssumeYes)
            {
                Console.WriteLine("[ERROR] Cannot use both --no (-n) and --yes (-y)");
                return null;
            }
            if (opts.FilePaths.Count > 1 && args.Any(a => a.StartsWith("-")))
            {
                Console.WriteLine("[ERROR] Extract mode only accepts one input file");
                return null;
            }

            if (!ResolvePaths(opts, args.Any(a => a.StartsWith("-"))))
                return null;

            return opts;
        }

        /// Determines all output paths on the Options object based on the input file
        static bool ResolvePaths(Options opts, bool hasOptions)
        {
            opts.IsoPath = opts.FilePaths[0];
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

            // TODO: Add SabreTools.Serialization
            if (opts.ExtractZAR || Path.GetExtension(opts.IsoPath).Equals(".zar", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("ZArchive support is coming soon!");
                return false;
            }

            string isoBasePath = Path.Combine(dir, $"{filename}.iso");
            opts.RedumpPath = (isoBasePath == opts.IsoPath || File.Exists(isoBasePath)) ? Path.Combine(dir, $"{filename}.redump.iso") : isoBasePath;
            opts.XrdPath = Path.Combine(dir, $"{filename}.xrd");
            opts.OutputPath = Path.Combine(dir, $"{filename}");
            opts.SkeletonPath = Path.Combine(dir, $"{filename}.skeleton.xiso");
            opts.FillerPath = Path.Combine(dir, $"{filename}.filler");
            opts.SeedPath = Path.Combine(dir, $"{filename}.seed");
            opts.SectorsTXTPath = Path.Combine(dir, "sectors.txt");
            opts.UpdatePath = Path.Combine(dir, "su20076000_00000000");
            opts.VideoPath = Path.Combine(dir, $"{filename}.video.iso");
            opts.XisoPath = Path.Combine(dir, $"{filename}.xiso");
            opts.ZarPath = Path.Combine(dir, $"{filename}.zar");

            // Detect additional input files by size/extension
            for (int f = 1; f < opts.FilePaths.Count; f++)
            {
                string fp = opts.FilePaths[f];
                if (!File.Exists(fp))
                {
                    Console.WriteLine($"[ERROR] Invalid file path: {fp}");
                    return false;
                }
                long fpSize = new FileInfo(fp).Length;
                if (Array.IndexOf(LibXGD.XGD.VIDEO_LENGTH, fpSize) >= 0 || fp.EndsWith(".video.iso", StringComparison.OrdinalIgnoreCase))
                    opts.VideoPath = fp;
                else if (fp.EndsWith(".seed", StringComparison.OrdinalIgnoreCase) || fpSize == 4)
                    opts.SeedPath = fp;
                else if (Path.GetFileName(fp).StartsWith("su200760", StringComparison.OrdinalIgnoreCase))
                    opts.UpdatePath = fp;
                else
                    opts.FillerPath = fp;
            }

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

            // Determine mode
            if (opts.RedumpIsoType >= 0)
                opts.Mode = Mode.ExtractRedump;
            else if (opts.VideoIsoType >= 0)
                opts.Mode = Mode.ExtractVideo;
            else if (opts.FilePaths.Count == 1 && hasOptions)
                opts.Mode = Mode.ProcessXISO;
            else
                opts.Mode = Mode.RebuildISO;

            return true;
        }
    }
}
