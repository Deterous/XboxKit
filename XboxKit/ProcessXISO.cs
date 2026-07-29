using System;
using System.IO;
using LibXGD;

namespace XboxKit
{
    internal static class ProcessXISO
    {
        static bool Validate(Options opts)
        {
            if (!opts.AssumeYes && (opts.AssumeNo || !opts.Quiet))
            {
                bool invalidOptions = false;
                if (opts.ExtractXRD)
                {
                    Console.WriteLine("[INFO] Cannot extract XRD (-m), input file is not a redump ISO.");
                    invalidOptions = true;
                }
                if (opts.ExtractXISO)
                {
                    Console.WriteLine("[INFO] Cannot extract XISO (-x), input file is not a redump ISO.");
                    invalidOptions = true;
                }
                if (opts.ExtractVideo)
                {
                    Console.WriteLine("[INFO] Cannot extract video (-v), input file is not a redump ISO.");
                    invalidOptions = true;
                }
                if (opts.ExtractUpdate)
                {
                    Console.WriteLine("[INFO] Cannot extract update (-u), input file is not a redump ISO.");
                    invalidOptions = true;
                }
                if (invalidOptions && opts.AssumeNo)
                    return false;
            }

            // Check that files don't already exist
            if (!opts.AssumeYes && (opts.WipeXISO || opts.TrimXISO || opts.ExtractSkeleton) && !Helpers.ConfirmOverwrite(opts.XisoPath, opts.AssumeNo))
                return false;
            if (!opts.AssumeYes && opts.ExtractFiller && !Helpers.ConfirmOverwrite(opts.FillerPath, opts.AssumeNo))
                return false;
            if (!opts.AssumeYes && opts.ExtractSeed && !Helpers.ConfirmOverwrite(opts.SeedPath, opts.AssumeNo))
                return false;
            if (!opts.AssumeYes && opts.ExtractSkeleton && !Helpers.ConfirmOverwrite(opts.HashPath, opts.AssumeNo))
                return false;
            if (!opts.AssumeYes && opts.ExtractZAR && !Helpers.ConfirmOverwrite(opts.ZarPath, opts.AssumeNo))
                return false;

            return true;
        }

        public static void Run(Options opts)
        {
            if (!Validate(opts))
                return;

            bool writeXISO = opts.WipeXISO || opts.TrimXISO || opts.ExtractSkeleton;
            if (!writeXISO && !opts.ExtractFiller && !opts.ExtractSeed && !opts.OutputFiles && !opts.ExtractZAR)
            {
                if (!opts.Quiet)
                    Console.WriteLine("[INFO] No applicable options for XISO input.");
                return;
            }

            // Open XISO for reading
            using FileStream isoFS = new(opts.IsoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!opts.Quiet) Console.WriteLine($"[INFO] Reading XISO from {opts.IsoPath}");

            // Validate XISO magic
            if (!XDVDFS.IsValidXISO(isoFS))
            {
                Console.WriteLine("[ERROR] Invalid XISO file");
                return;
            }

            // Extract game files (works on trimmed XISOs)
            if (opts.OutputFiles)
            {
                var wrapper = SabreTools.Wrappers.XboxISO.Create(isoFS);
                if (wrapper is null)
                {
                    Console.WriteLine("[ERROR] Invalid ISO");
                    return;
                }

                if (!Directory.Exists(opts.OutputPath))
                    Directory.CreateDirectory(opts.OutputPath);

                if (!opts.Quiet) Console.WriteLine($"[INFO] Outputting game files to {opts.OutputPath}");
                if (!wrapper.ExtractGamePartition(opts.OutputPath, !opts.Quiet))
                {
                    Console.WriteLine($"[ERROR] Failed to extract files from {opts.IsoPath}");
                    return;
                }
            }

            // Create ZArchive of game files (works on trimmed XISOs)
            if (opts.ExtractZAR)
            {
                if (!opts.Quiet) Console.WriteLine($"[INFO] Creating ZArchive at {opts.ZarPath}");
                if (!ZArchive.CreateZAR(isoFS, 0, opts.ZarPath, false, opts.Quiet))
                {
                    Console.WriteLine("[ERROR] Failed creating ZArchive.");
                    return;
                }
            }

            // If XGD1, try brute force the filler data seed
            if (opts.ExtractSeed && opts.XGDType == 0)
            {
                uint? seed = XboxPRNG.ExtractSeed(isoFS, 0, opts.Quiet);
                if (seed.HasValue)
                {
                    if (!opts.Quiet) Console.WriteLine($"[INFO] Filler data seed: {seed.Value:X8}");
                    using FileStream seedFS = new(opts.SeedPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    byte[] seedBytes = BitConverter.GetBytes(seed.Value);
                    seedFS.Write(seedBytes, 0, seedBytes.Length);
                    if (!opts.Quiet) Console.WriteLine($"[INFO] Writing filler data seed to {opts.SeedPath}");
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

            // Cannot wipe/trim/extract filler from unknown XISO size
            if (opts.XisoType < 0 && (writeXISO || opts.ExtractFiller))
            {
                Console.WriteLine("[ERROR] Unexpected XISO size. Your file may be trimmed or corrupt.");
                Console.WriteLine("        Use the full XISO if you want to trim/wipe/extract filler.");
                return;
            }

            // Create file for game partition
            if (writeXISO && opts.WipeXISO && !opts.Quiet)
                Console.WriteLine($"[INFO] Writing wiped XISO to {opts.XisoPath}");
            else if (writeXISO && opts.TrimXISO && !opts.Quiet)
                Console.WriteLine($"[INFO] Writing XISO to {opts.XisoPath}");
            using FileStream? xisoFS = writeXISO ? new FileStream(opts.XisoPath, FileMode.Create, FileAccess.Write, FileShare.None) : null;

            // Create file for filler data
            if (opts.ExtractFiller && !opts.Quiet)
                Console.WriteLine($"[INFO] Extracting filler data to {opts.FillerPath}");
            using FileStream? fillerFS = opts.ExtractFiller ? new FileStream(opts.FillerPath, FileMode.Create, FileAccess.Write, FileShare.None) : null;

            // Create hash file for skeleton
            if (opts.ExtractSkeleton && !opts.Quiet)
                Console.WriteLine($"[INFO] Hashing game files to {opts.HashPath}");
            using StreamWriter? hashWriter = opts.ExtractSkeleton ? new StreamWriter(opts.HashPath, false, System.Text.Encoding.UTF8) : null;

            // Process XISO
            if (!XDVDFS.ProcessXISO(isoFS, 0, opts.IsoSize, xisoFS, fillerFS, opts.WipeXISO, opts.TrimXISO, opts.ExtractSkeleton, opts.Quiet, hashWriter))
            {
                Console.WriteLine("[ERROR] Failed processing XISO.");
                return;
            }
        }
    }
}
