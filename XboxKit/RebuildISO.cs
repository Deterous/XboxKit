using System;
using System.IO;
using LibXGD;

namespace XboxKit
{
    internal static class RebuildISO
    {
        static int[]? ParseSecuritySectors(Options opts, long redumpLength)
        {
            if (!File.Exists(opts.SectorsTXTPath))
            {
                Console.WriteLine("[ERROR] To rebuild from an initial seed, a list of security sector ranges is needed in sectors.txt");
                return null;
            }
            if (!opts.Quiet) Console.WriteLine($"[INFO] Reading security sector ranges {opts.SectorsTXTPath}");

            int[] securitySectors = new int[16];
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
                        return null;
                    }
                    securitySectors[i] = startSector;
                    i += 1;
                }
                else
                {
                    Console.WriteLine("[ERROR] Invalid format of sectors.txt");
                    return null;
                }
            }
            return securitySectors;
        }

        static bool Validate(Options opts)
        {
            // Check that video partition exists
            if (!File.Exists(opts.VideoPath))
            {
                Console.WriteLine($"[ERROR] Invalid file path: {opts.VideoPath}");
                Console.WriteLine("         Provide a file path to the video partition to rebuild the redump ISO.");
                return false;
            }

            // TODO: Allow for rebuilding with trimmed XISO file without filler data, even if it will not match redump?
            if (opts.XisoType < 0 && !File.Exists(opts.FillerPath) && !File.Exists(opts.SeedPath))
            {
                Console.WriteLine("[ERROR] Unexpected XISO size. Your XISO may be trimmed or corrupt.");
                Console.WriteLine("        Cannot rebuild redump ISO from trimmed XISO without filler or seed.");
                return false;
            }

            if (opts.VideoType < 0)
            {
                Console.WriteLine("[ERROR] Unexpected video partition ISO size. Your video file may be trimmed or corrupt.");
                return false;
            }

            // Check that redump ISO doesn't already exist
            if (!opts.AssumeYes && !Helpers.ConfirmOverwrite(opts.RedumpPath, opts.AssumeNo))
                return false;

            return true;
        }

        // Calculate expected filler size from XDVDFS ranges
        static long GetExpectedFillerSize(FileStream isoFS, long xisoLength)
        {
            var validRanges = XDVDFS.GetXISORanges(isoFS, 0, true);
            long validBytes = 0;
            foreach (var (start, end) in validRanges.All)
                validBytes += (end - start + 1) * XDVDFS.SECTOR_SIZE;
            isoFS.Seek(0, SeekOrigin.Begin);
            return xisoLength - validBytes;
        }

        public static void Run(Options opts)
        {
            if (!Validate(opts))
                return;

            // Open XISO for reading
            using FileStream isoFS = new(opts.IsoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!opts.Quiet) Console.WriteLine($"[INFO] Reading XISO from {opts.IsoPath}");

            // Validate XISO
            if (!XDVDFS.IsValidXISO(isoFS))
            {
                Console.WriteLine($"[ERROR] Invalid XISO file: {opts.IsoPath}");
                return;
            }

            // Validate filler file
            if (File.Exists(opts.FillerPath) && opts.XisoType >= 0)
            {
                long xisoLength = XGD.XISO_LENGTH[opts.XisoType];
                long expectedFillerSize = GetExpectedFillerSize(isoFS, xisoLength);
                long actualFillerSize = new FileInfo(opts.FillerPath).Length;

                // TODO: Allow for RC4 format file + sectors.txt / SS.bin
                if (actualFillerSize != expectedFillerSize)
                {
                    Console.WriteLine($"[ERROR] Random filler data should be {expectedFillerSize} bytes, got {actualFillerSize} bytes");
                    if (actualFillerSize < expectedFillerSize)
                        Console.WriteLine("        The filler file should contain the zeroed security sector ranges, not just the RC4 data!");
                    return;
                }
            }

            // Create redump ISO
            using FileStream redumpFS = new(opts.RedumpPath, FileMode.Create, FileAccess.Write, FileShare.None);
            if (!opts.Quiet) Console.WriteLine($"[INFO] Writing redump ISO to {opts.RedumpPath}");

            // Open video ISO for reading
            using FileStream videoFS = new(opts.VideoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!opts.Quiet) Console.WriteLine($"[INFO] Reading video partition from {opts.VideoPath}");

            // Open filler data for reading if available
            if (File.Exists(opts.FillerPath) && !opts.Quiet)
                Console.WriteLine($"[INFO] Reading random filler data from {opts.FillerPath}");
            using FileStream? rebuildFillerFS = File.Exists(opts.FillerPath) ? new FileStream(opts.FillerPath, FileMode.Open, FileAccess.Read, FileShare.Read) : null;

            // Get XGD1 initial seed, if path exists
            XboxPRNG prng = null!;
            if (rebuildFillerFS == null && opts.XGDType == 0 && File.Exists(opts.SeedPath))
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
            if (rebuildFillerFS == null && opts.XGDType == 0 && prng == null && File.Exists(opts.FillerPath))
            {
                FileInfo seedInfo = new(opts.FillerPath);
                if (seedInfo.Length == 4)
                {
                    using FileStream seedFS = new(opts.FillerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (!opts.Quiet) Console.WriteLine($"[INFO] Reading initial seed from {opts.FillerPath}");
                    prng = new(Utils.ReadUInt(seedFS));
                }
            }

            // Parse sectors.txt if an initial seed is being used
            int[] securitySectors = new int[16];
            if (rebuildFillerFS == null && opts.XGDType == 0 && prng != null)
            {
                long redumpLength = XGD.GetRedumpLength(opts.VideoType);
                int[]? parsed = ParseSecuritySectors(opts, redumpLength);
                if (parsed == null)
                    return;
                securitySectors = parsed;
            }

            // Open system update file if available
            if (File.Exists(opts.UpdatePath) && !opts.Quiet)
                Console.WriteLine($"[INFO] Reading system update from {opts.UpdatePath}");
            using FileStream? updateFS = File.Exists(opts.UpdatePath) ? new FileStream(opts.UpdatePath, FileMode.Open, FileAccess.Read, FileShare.Read) : null;

            // Rebuild redump ISO
            if (!XGD.RebuildRedump(isoFS, redumpFS, videoFS, rebuildFillerFS, updateFS, prng, securitySectors, opts.VideoType, opts.Quiet))
            {
                Console.WriteLine("[ERROR] Failed rebuilding redump ISO.");
                return;
            }
        }
    }
}
