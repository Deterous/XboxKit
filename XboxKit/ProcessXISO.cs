using System;
using System.IO;
using LibXGD;

namespace XboxKit
{
    internal static class ProcessXISO
    {
        public static void Run(Options opts)
        {
            // TODO: Validate file is an XISO (other than filesize)
            // TODO: If ZAR is input, extract

            // Check for invalid options
            if (!opts.AssumeYes && (opts.AssumeNo || !opts.Quiet))
            {
                bool invalidOptions = false;
                if (opts.ExtractXISO)
                {
                    Console.WriteLine("[INFO] Cannot extract XISO (-x), input file is already XISO (or unexpected ISO).");
                    invalidOptions = true;
                }
                if (opts.ExtractVideo)
                {
                    Console.WriteLine("[INFO] Cannot extract video (-v), input file is XISO (or unexpected ISO).");
                    invalidOptions = true;
                }
                if (opts.ExtractUpdate)
                {
                    Console.WriteLine("[INFO] Cannot extract update (-u), input file is XISO (or unexpected ISO).");
                    invalidOptions = true;
                }
                if (invalidOptions && opts.AssumeNo)
                    return;
            }

            // Open XISO for reading
            using FileStream isoFS = new(opts.IsoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!opts.Quiet) Console.WriteLine($"[INFO] Reading XISO from {opts.IsoPath}");

            bool writeXISO = opts.WipeXISO || opts.TrimXISO || opts.ExtractSkeleton;
            if (opts.ExtractFiller || writeXISO)
            {
                // Cannot extract/wipe/trim from invalid XISO size
                if (opts.XisoType < 0)
                {
                    Console.WriteLine("[ERROR] Unexpected XISO size. Your file may be trimmed or corrupt.");
                    Console.WriteLine("        Use the full XISO if you want to trim/wipe/extract filler.");
                    return;
                }

                // Create file for game partition
                FileStream xisoFS = null!;
                if (writeXISO)
                {
                    if (opts.WipeXISO && !opts.Quiet)
                        Console.WriteLine($"[INFO] Writing wiped XISO to {opts.XisoPath}");
                    else if (opts.TrimXISO && !opts.Quiet)
                        Console.WriteLine($"[INFO] Writing XISO to {opts.XisoPath}");
                    xisoFS = new FileStream(opts.XisoPath, FileMode.Create, FileAccess.Write, FileShare.None);
                }

                // Create file for filler data
                FileStream fillerFS = null!;
                if (opts.ExtractFiller)
                {
                    if (!opts.Quiet) Console.WriteLine($"[INFO] Extracting filler data to {opts.FillerPath}");
                    fillerFS = new FileStream(opts.FillerPath, FileMode.Create, FileAccess.Write, FileShare.None);
                }

                // Process XISO
                if (!XDVDFS.ProcessXISO(isoFS, 0, opts.IsoSize, xisoFS, fillerFS, opts.WipeXISO, opts.TrimXISO, opts.ExtractSkeleton, opts.Quiet))
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

            // Rebuild Redump ISO

            // Check that video partition exists
            if (!File.Exists(opts.VideoPath))
            {
                Console.WriteLine($"[ERROR] Invalid file path: {opts.VideoPath}");
                Console.WriteLine("         Provide a file path to the video partition to rebuild the redump ISO.");
                return;
            }

            // TODO: Allow for rebuilding with trimmed XISO file without filler data
            if (opts.XisoType < 0 && !File.Exists(opts.FillerPath) && !File.Exists(opts.SeedPath))
            {
                Console.WriteLine("[ERROR] Unexpected XISO size. Your XISO may be trimmed or corrupt.");
                Console.WriteLine("        Cannot rebuild redump ISO from trimmed XISO without filler or seed.");
                return;
            }

            // Determine video type based on video partition size
            FileInfo videoInfo = new(opts.VideoPath);
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
            using FileStream redumpFS = new(opts.RedumpPath, FileMode.Create, FileAccess.Write, FileShare.None);
            if (!opts.Quiet) Console.WriteLine($"[INFO] Writing redump ISO to {opts.RedumpPath}");

            // Open video ISO for reading
            using FileStream videoFS = new(opts.VideoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!opts.Quiet) Console.WriteLine($"[INFO] Reading video partition from {opts.VideoPath}");

            // Open filler data for reading if available
            FileStream rebuildFillerFS = null!;
            if (File.Exists(opts.FillerPath))
            {
                rebuildFillerFS = new(opts.FillerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!opts.Quiet) Console.WriteLine($"[INFO] Reading random filler data from {opts.FillerPath}");
            }

            // Get XGD1 initial seed, if path exists
            XboxPRNG prng = null!;
            if (rebuildFillerFS == null && rebuildXisoType == 0 && File.Exists(opts.SeedPath))
            {
                FileInfo seedInfo = new(opts.SeedPath);
                if (seedInfo.Length == 4)
                {
                    using FileStream seedFS = new(opts.SeedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (!opts.Quiet) Console.WriteLine($"[INFO] Reading initial seed from {opts.SeedPath}");
                    prng = new(Utils.ReadUInt(seedFS));
                }
            }

            // Check fillerPath for initial seed
            if (rebuildFillerFS == null && rebuildXisoType == 0 && prng == null && File.Exists(opts.FillerPath))
            {
                FileInfo seedInfo = new(opts.FillerPath);
                if (seedInfo.Length == 4)
                {
                    using FileStream seedFS = new(opts.FillerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (!opts.Quiet) Console.WriteLine($"[INFO] Reading initial seed from {opts.FillerPath}");
                    prng = new(Utils.ReadUInt(seedFS));
                }
            }

            // Open sectors.txt if an initial seed is being used
            int[] securitySectors = new int[16];
            if (rebuildFillerFS == null && rebuildXisoType == 0 && prng != null)
            {
                long redumpLength = XGD.GetRedumpLength(videoType);
                if (!File.Exists(opts.SectorsTXTPath))
                {
                    Console.WriteLine("[ERROR] To rebuild from an initial seed, a list of security sector ranges is needed in sectors.txt");
                    return;
                }
                if (!opts.Quiet) Console.WriteLine($"[INFO] Reading security sector ranges {opts.SectorsTXTPath}");
                using FileStream sectorsFS = new(opts.SectorsTXTPath, FileMode.Open, FileAccess.Read, FileShare.Read);
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
            if (!XGD.RebuildRedump(isoFS, redumpFS, videoFS, rebuildFillerFS, prng, securitySectors, videoType, opts.Quiet))
            {
                Console.WriteLine("[ERROR] Failed rebuilding redump ISO.");
                return;
            }

            // Close filler file
            if (rebuildFillerFS != null)
                rebuildFillerFS.Dispose();

            // Insert system update file if available
            if (!XGD.RebuildWithUpdate(redumpFS, videoFS, opts.UpdatePath, videoType, opts.Quiet))
            {
                Console.WriteLine("[ERROR] Failed writing system update file.");
                return;
            }
        }
    }
}
