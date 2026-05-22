using System;
using System.IO;
using LibXGD;

namespace XboxKit
{
    internal static class ExtractRedump
    {
        static bool Validate(Options opts)
        {
            // Must be doing something
            if (!opts.OptionsProvided)
            {
                Console.WriteLine("[ERROR] Redump ISO provided with no options, nothing to do");
                Console.WriteLine("");
                Helpers.PrintHelp();
                return false;
            }

            // Can't create both XISO and XISO Skeleton
            if (opts.ExtractXISO && opts.ExtractSkeleton)
            {
                Console.WriteLine("[ERROR] Cannot create both XISO (-x) and XISO Skeleton (-p)");
                Console.WriteLine("        Skeleton zeroes game files, typically used with -o or -z");
                Console.WriteLine("");
                return false;
            }

            // Must extract video if also extracting SU
            if (!opts.AssumeYes && opts.ExtractUpdate && !opts.ExtractVideo)
            {
                Console.WriteLine("[ERROR] Extracting update (-u) implies extract video (-v)");
                if (opts.AssumeNo)
                    return false;
                Console.WriteLine($"Would you like to also extract Video? (Y/N)");
                string? response = Console.ReadLine()?.ToUpper();
                if (response != "Y" && response != "YES")
                    return false;
                opts.ExtractVideo = true;
            }

            // Check option combination is valid
            if (!opts.AssumeYes && opts.WipeXISO && !(opts.ExtractXISO || opts.ExtractSkeleton) && (opts.AssumeNo || !opts.Quiet))
            {
                Console.WriteLine("[INFO] Wiping XISO option (-w) does nothing without extracting XISO (-x) or skeleton (-p)");
                if (opts.AssumeNo)
                    return false;
            }
            if (!opts.AssumeYes && opts.TrimXISO && !(opts.ExtractXISO || opts.ExtractSkeleton) && (opts.AssumeNo || !opts.Quiet))
            {
                Console.WriteLine("[INFO] Trimming XISO option (-t) does nothing without extracting XISO (-x) or skeleton (-p)");
                if (opts.AssumeNo)
                    return false;
            }
            if (!opts.AssumeYes && (opts.ExtractXISO || opts.ExtractSkeleton) && opts.ExtractFiller && !opts.WipeXISO && (opts.AssumeNo || !opts.Quiet))
            {
                Console.WriteLine("[INFO] Cannot write filler data without wiping XISO");
                Console.WriteLine("       For now, use -w with -r");
                if (opts.AssumeNo)
                    return false;
            }

            // Check that files don't already exist
            if (!opts.AssumeYes && opts.ExtractXISO && !Helpers.ConfirmOverwrite(opts.XisoPath, opts.AssumeNo))
                return false;
            if (!opts.AssumeYes && opts.ExtractSkeleton && !Helpers.ConfirmOverwrite(opts.SkeletonPath, opts.AssumeNo))
                return false;
            if (!opts.AssumeYes && opts.ExtractVideo && !Helpers.ConfirmOverwrite(opts.VideoPath, opts.AssumeNo))
                return false;
            if (!opts.AssumeYes && opts.ExtractFiller && !Helpers.ConfirmOverwrite(opts.FillerPath, opts.AssumeNo))
                return false;
            if (!opts.AssumeYes && opts.ExtractUpdate && !Helpers.ConfirmOverwrite(opts.UpdatePath, opts.AssumeNo))
                return false;
            if (!opts.AssumeYes && opts.ExtractSeed && !Helpers.ConfirmOverwrite(opts.SeedPath, opts.AssumeNo))
                return false;
            if (!opts.AssumeYes && opts.ExtractXRD && !Helpers.ConfirmOverwrite(opts.XrdPath, opts.AssumeNo))
                return false;
            if (!opts.AssumeYes && opts.ExtractZAR && !Helpers.ConfirmOverwrite(opts.ZarPath, opts.AssumeNo))
                return false;
            if (!opts.AssumeYes && opts.ExtractZAR && !Helpers.ConfirmOverwrite(opts.HashPath, opts.AssumeNo))
                return false;

            return true;
        }

        public static void Run(Options opts)
        {
            if (!Validate(opts))
                return;

            // Open redump ISO for reading
            if (!opts.Quiet) Console.WriteLine($"[INFO] Reading redump ISO from {opts.IsoPath}");
            using FileStream isoFS = new(opts.IsoPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            if (opts.OutputFiles || opts.ExtractXRD)
            {
                var wrapper = SabreTools.Wrappers.XboxISO.Create(isoFS);
                if (wrapper is null)
                {
                    Console.WriteLine($"[ERROR] Invalid ISO");
                    return;
                }

                // Extract game files
                if (opts.OutputFiles)
                {
                    if (!opts.Quiet) Console.WriteLine($"[INFO] Parsing Xbox DVD filesystem");
                    isoFS.Seek(XGD.XISO_OFFSET[opts.XGDType], SeekOrigin.Begin);

                    if (!Directory.Exists(opts.OutputPath))
                        Directory.CreateDirectory(opts.OutputPath);

                    if (!opts.Quiet) Console.WriteLine($"[INFO] Outputting game files to {opts.OutputPath}");
                    if (!wrapper.ExtractGamePartition(opts.OutputPath, !opts.Quiet))
                    {
                        Console.WriteLine($"[ERROR] Failed to extract files from {opts.IsoPath}");
                        return;
                    }
                }

                // Extract rebuild data
                if (opts.ExtractXRD)
                {
                    // Create file for XRD
                    if (!opts.Quiet) Console.WriteLine($"[INFO] Writing XRD metadata file to {opts.XrdPath}");
                    var xrd = XRD.GetXRD(isoFS, wrapper, opts.RedumpIsoType);
                    if (xrd is null)
                    {
                        Console.WriteLine($"[ERROR] Failed to create XRD");
                        return;
                    }
                    var writer = new SabreTools.Serialization.Writers.XRD();
                    if(!writer.SerializeFile(xrd, opts.XrdPath))
                    {
                        Console.WriteLine($"[ERROR] Failed to write XRD");
                        return;
                    }
                }
            }

            // Extract video partition
            if (opts.ExtractVideo)
            {
                if (!opts.Quiet) Console.WriteLine($"[INFO] Writing video partition to {opts.VideoPath}");
                if (!XGD.ExtractVideo(isoFS, opts.VideoPath, opts.RedumpIsoType))
                {
                    Console.WriteLine($"[ERROR] Failed writing video partition.");
                    return;
                }
            }

            // Extract system update file from XGD3 video partition
            if (opts.ExtractUpdate && opts.XGDType == 3)
            {
                if (!opts.Quiet) Console.WriteLine($"[INFO] Writing system update file to {opts.UpdatePath}");
                if (!opts.Quiet) Console.WriteLine($"[INFO] Zeroing system update file in {opts.VideoPath}");
                if (!ExtractVideo.ExtractSU(opts.VideoPath, opts.UpdatePath))
                {
                    Console.WriteLine($"[ERROR] Failed writing system update file.");
                    return;
                }
            }

            // If XGD1, try brute force the filler data seed
            if (opts.ExtractSeed && opts.XGDType == 0)
            {
                uint? seed = XboxPRNG.ExtractSeed(isoFS, XGD.XISO_OFFSET[opts.XGDType], opts.Quiet);
                if (seed.HasValue)
                {
                    if (!opts.Quiet) Console.WriteLine($"[INFO] Filler data seed: {seed.Value:X8}");
                    using FileStream seedFS = new(opts.SeedPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    byte[] seedBytes = BitConverter.GetBytes(seed.Value);
                    seedFS.Write(seedBytes, 0, seedBytes.Length);
                    if (!opts.Quiet) Console.WriteLine($"[INFO] Writing filler data to {opts.SeedPath}");
                }
                else
                {
                    Console.WriteLine("[ERROR] Failed to extract XGD1 seed.");
                }
            }
            else if (opts.ExtractSeed)
            {
                if (!opts.Quiet) Console.WriteLine($"[INFO] Cannot extract seed from Xbox 360 discs");
            }

            // Quit early if we're not extracting data from game partition
            if (!opts.ExtractXISO && !opts.ExtractFiller && !opts.ExtractSkeleton)
                return;

            // Create file for game partition
            string? xisoPath = opts.ExtractXISO ? opts.XisoPath : opts.ExtractSkeleton ? opts.SkeletonPath : null;
            if (opts.ExtractXISO && !opts.Quiet)
                Console.WriteLine($"[INFO] Writing game partition to {opts.XisoPath}");
            else if (opts.ExtractSkeleton && !opts.Quiet)
                Console.WriteLine($"[INFO] Writing XISO skeleton to {opts.SkeletonPath}");
            using FileStream? xisoFS = xisoPath != null ? new FileStream(xisoPath, FileMode.Create, FileAccess.Write, FileShare.None) : null;

            // Create file for filler data
            if (opts.ExtractFiller && !opts.Quiet)
                Console.WriteLine($"[INFO] Writing random filler data to {opts.FillerPath}");
            using FileStream? fillerFS = opts.ExtractFiller ? new FileStream(opts.FillerPath, FileMode.Create, FileAccess.Write, FileShare.None) : null;

            // Process XISO
            if (!XDVDFS.ProcessXISO(isoFS, XGD.XISO_OFFSET[opts.XGDType], XGD.XISO_LENGTH[opts.XGDType], xisoFS!, fillerFS!, opts.WipeXISO, opts.TrimXISO, opts.ExtractSkeleton, opts.Quiet))
            {
                Console.WriteLine("[ERROR] Failed processing XISO.");
                return;
            }
        }
    }
}
