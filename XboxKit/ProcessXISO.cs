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

            return true;
        }

        public static void Run(Options opts)
        {
            if (!Validate(opts))
                return;

            bool writeXISO = opts.WipeXISO || opts.TrimXISO || opts.ExtractSkeleton;
            if (!writeXISO && !opts.ExtractFiller && !opts.ExtractSeed && !opts.ExtractFiles && !opts.ExtractZAR)
            {
                if (!opts.Quiet) Console.WriteLine("[ERROR] XISO file provided but nothing to do.");
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

            // Cannot extract/wipe/trim from invalid XISO size
            if (opts.XisoType < 0)
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

            // Process XISO
            if (!XDVDFS.ProcessXISO(isoFS, 0, opts.IsoSize, xisoFS, fillerFS, opts.WipeXISO, opts.TrimXISO, opts.ExtractSkeleton, opts.Quiet))
            {
                Console.WriteLine("[ERROR] Failed processing XISO.");
                return;
            }
        }
    }
}
