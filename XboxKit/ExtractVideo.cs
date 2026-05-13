using System;
using System.IO;
using LibXGD;

namespace XboxKit
{
    internal static class ExtractVideo
    {
        // Heuristic to determine XGD3 system update file offset in video partition
        // This algorithm is easier than parsing UDF
        static long SUOffset(FileStream videoFS)
        {
            long updateOffset = videoFS.Length;
            byte[] videoBuf = new byte[16];
            while (updateOffset >= XDVDFS.SECTOR_SIZE)
            {
                videoFS.Seek(updateOffset - XDVDFS.SECTOR_SIZE, SeekOrigin.Begin);
                int bytesRead = 0;
                while (bytesRead < videoBuf.Length)
                {
                    int n = videoFS.Read(videoBuf, bytesRead, videoBuf.Length - bytesRead);
                    if (n == 0)
                        break;
                    bytesRead += n;
                }
                if (XDVDFS.FILLER.AsSpan().SequenceEqual(videoBuf))
                    break;

                updateOffset -= XDVDFS.SECTOR_SIZE;
            }
            return updateOffset;
        }

        // Extracts and zeroes the SU file from Video ISO
        internal static bool ExtractSU(string isoPath, string updatePath)
        {
            using FileStream videoFS = new(isoPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            long updateOffset = SUOffset(videoFS);

            using FileStream updateFS = new(updatePath, FileMode.Create, FileAccess.Write, FileShare.None);
            long updateLength = videoFS.Length - updateOffset - XDVDFS.SECTOR_SIZE;
            if (!Utils.WriteBytes(videoFS, updateFS, updateOffset, updateLength))
                return false;

            // Zero out the update file in the video ISO
            Utils.WriteZeroes(videoFS, updateOffset, updateLength);

            return true;
        }
        public static void Run(Options opts)
        {
            if (!opts.ExtractUpdate)
            {
                Console.WriteLine("[ERROR] Use -u flag to extract system update from video partition.");
                return;
            }

            // Check for valid options
            if (!opts.AssumeYes && (opts.AssumeNo || !opts.Quiet))
            {
                bool invalidOptions = false;
                if (opts.ExtractVideo)
                {
                    Console.WriteLine("[INFO] Cannot extract video (-v), input file is already video ISO.");
                    invalidOptions = true;
                }
                if (opts.ExtractXISO)
                {
                    Console.WriteLine("[INFO] Cannot extract XISO (-x), input file is video ISO.");
                    invalidOptions = true;
                }
                if (opts.ExtractFiller)
                {
                    Console.WriteLine("[INFO] Cannot extract filler (-r), input file is video ISO.");
                    invalidOptions = true;
                }
                if (opts.WipeXISO)
                {
                    Console.WriteLine("[INFO] Cannot wipe XISO (-w), input file is video ISO.");
                    invalidOptions = true;
                }
                if (opts.TrimXISO)
                {
                    Console.WriteLine("[INFO] Cannot trim XISO (-t), input file is video ISO.");
                    invalidOptions = true;
                }
                if (invalidOptions && opts.AssumeNo)
                    return;
            }

            // Check that update file doesn't already exist
            if (!opts.AssumeYes && opts.ExtractUpdate && !Helpers.ConfirmOverwrite(opts.UpdatePath, opts.AssumeNo))
                return;

            // Check that video partition is from XGD3 disc
            if (opts.VideoIsoType != 16 && opts.VideoIsoType != 17 && opts.VideoIsoType != 18)
            {
                Console.WriteLine("[ERROR] Can only extract su20076000_00000000 from XGD3 video partitions.");
                return;
            }

            if (!opts.Quiet) Console.WriteLine($"[INFO] Writing system update file to {opts.UpdatePath}");
            if (!opts.Quiet) Console.WriteLine($"[INFO] Zeroing system update file in {opts.IsoPath}");
            if (!ExtractSU(opts.IsoPath, opts.UpdatePath))
            {
                Console.WriteLine($"[ERROR] Failed writing system update file.");
                return;
            }
        }
    }
}
