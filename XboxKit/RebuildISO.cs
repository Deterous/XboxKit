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

            List<int> securitySectors = new();
            using FileStream sectorsFS = new(opts.SectorsTXTPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using StreamReader sectorsSR = new StreamReader(sectorsFS);
            string? line;
            int lineCount = 0;
            while ((line = sectorsSR.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] range = line.Split('-');
                if (range.Length == 2 && int.TryParse(range[0], out int startSector) && int.TryParse(range[1], out int endSector))
                {
                    if (startSector < 0 || startSector > (redumpLength / XDVDFS.SECTOR_SIZE - 4096) || endSector - startSector != 4095)
                    {
                        Console.WriteLine("[ERROR] Invalid security sectors in sectors.txt");
                        return null;
                    }
                    lineCount++;
                    // For XGD2/3, only keep the first range (the one within the XISO)
                    if (opts.XGDType == 0 || lineCount == 1)
                        securitySectors.Add(startSector);
                }
                else
                {
                    Console.WriteLine("[ERROR] Invalid format of sectors.txt");
                    return null;
                }
            }

            if (opts.XGDType == 0 && lineCount != 16)
            {
                Console.WriteLine($"[ERROR] Expected 16 security sector ranges in sectors.txt, got {lineCount}");
                return null;
            }
            if (opts.XGDType != 0 && lineCount != 1 && lineCount != 2)
            {
                Console.WriteLine($"[ERROR] Expected 1 or 2 security sector ranges in sectors.txt, got {lineCount}");
                return null;
            }

            return securitySectors.ToArray();
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
            var (sysRanges, fileRanges) = XDVDFS.GetXISORanges(isoFS, 0, true);
            var allRanges = XDVDFS.MergeRanges(sysRanges, fileRanges);
            long validBytes = 0;
            foreach (var (start, end) in allRanges)
                validBytes += (end - start + 1) * XDVDFS.SECTOR_SIZE;
            isoFS.Seek(0, SeekOrigin.Begin);
            return xisoLength - validBytes;
        }

        // Calculate total bytes of security sectors within the XISO that would be skipped
        static long GetSkippedSecuritySectorBytes(Options opts, long xisoLength)
        {
            if (!File.Exists(opts.SectorsTXTPath))
                return 0;

            long redumpLength = XGD.GetRedumpLength(opts.VideoType);
            int[]? securitySectors = ParseSecuritySectors(opts, redumpLength);
            if (securitySectors == null)
                return 0;

            long xisoOffset = XGD.XISO_OFFSET[opts.XisoType];
            long xisoStartSector = xisoOffset / XDVDFS.SECTOR_SIZE;
            long xisoEndSector = (xisoOffset + xisoLength) / XDVDFS.SECTOR_SIZE;
            int count = 0;

            foreach (int startSector in securitySectors)
            {
                if (startSector >= xisoStartSector && startSector + 4096 <= xisoEndSector)
                    count++;
            }

            return count * 4096 * XDVDFS.SECTOR_SIZE;
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

                if (actualFillerSize != expectedFillerSize)
                {
                    // Check if filler excludes security sector ranges (RC4 format)
                    long skippedBytes = GetSkippedSecuritySectorBytes(opts, xisoLength);
                    if (skippedBytes > 0 && actualFillerSize == expectedFillerSize - skippedBytes)
                    {
                        if (!opts.Quiet) Console.WriteLine($"[INFO] Filler file excludes {skippedBytes} bytes of security sector ranges.");
                    }
                    else
                    {
                        Console.WriteLine($"[ERROR] Random filler data should be {expectedFillerSize} bytes, got {actualFillerSize} bytes");
                        long securitySectorBytes = (opts.XGDType == 0 ? 16 : 1) * 4096 * XDVDFS.SECTOR_SIZE;
                        if (actualFillerSize == expectedFillerSize - securitySectorBytes)
                            Console.WriteLine("        The filler file may be missing security sector ranges. Provide a sectors.txt file with the sector ranges.");
                        return;
                    }
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
            XboxPRNG? prng = null;
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
