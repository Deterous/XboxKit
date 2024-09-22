using System;
using System.IO;
using System.Text;

namespace XboxKit
{
    internal class Program
    {
        static void Main(string[] args)
        {

            long[] REDUMP_ISO_LENGTH = [0x1D26A8000, 0x1D3301800, 0x1D2FEF800, 0x1D3082000, 0x1D3390000, 0x1D31A0000, 0x208E05800, 0x208E03800];  // XGD1, XGD2w0, XGD2w1, XGD2w2, XGD2w3+, XGD2-Hybrid, XGD3v0, XGD3
            long[] XISO_OFFSET = [0x18300000, 0xFD90000, 0x89D80000, 0x2080000]; // XGD1, XGD2, XGD2-Hybrid, XGD3
            long[] XISO_LENGTH = [0x1A2DB0000, 0x1B3880000, 0xBF8A0000, 0x204510000]; // XGD1, XGD2, XGD2-Hybrid, XGD3
            long[] VIDEO_L0_LENGTH = [0xD58000, 0xA8000, 0x548000, 0x438000, 0x4BB0000, 0x56C0000, 0x5460000, 0x5BA0000, 0x5C10000, 0x55D0000, 0x55C0000, 0x8A40000, 0x8A90000, 0x8E80000, 0x4B1D0000, 0x1880000, 0x1880000]; // XGD1, XGD2w0, XGD2w1, XGD2w2, XGD2w3, XGD2w4-7, XGD2w8-9, XGD2w10-12, XGD2w13, XGD2w14-15, XGD2w16, XGD2w17-18, XGD2w19, XGD2w20, XGD2-Hybrid, XGD3v0, XGD3
            long[] VIDEO_L1_LENGTH = [0x50000, 0x9800, 0x197800, 0x11A000, 0x4BA0000, 0x56B0000, 0x5450000, 0x5B90000, 0x5C00000, 0x55C0000, 0x55B0000, 0x8A30000, 0x8A80000, 0x8E70000, 0x4AFD0000, 0x1875800, 0x1873800]; // XGD1, XGD2w0, XGD2w1, XGD2w2, XGD2w3, XGD2w4-7, XGD2w8-9, XGD2w10-12, XGD2w13, XGD2w14-15, XGD2w16, XGD2w17-18, XGD2w19, XGD2w20, XGD2-Hybrid, XGD3v0, XGD3
            string[] WAVE_PVD = ["2004083110334900", "2005100712184600", "2006030621090700", "2009011416000000", "2009082417000000", "2009100517000000", "2009102917000000", "2010022116000000", "2010090417000000", "2010091517000000", "2010102817000000", "2011011816000000", "2011061217000000", "2011071217000000", "2011120716000000", "2012022116000000", "2012062117000000", "2012110716000000", "2012111816000000", "2013082617000000", "2015042617000000", "2006041012132800"]; // Wave 0-20, and XGD2 Hybrid

            // Check arguments
            if ((args.Length == 0) || (args.Length > 2))
            {
                Console.WriteLine("Usage: xboxkit.exe <input.iso> [<video.iso>]");
                Console.WriteLine("Converts a redump xbox ISO to its game (xiso) and video partitions.");
                return;
            }
            string isoPath = args[0];
            string videoPath = null;
            if (args.Length == 2)
                videoPath = args[1];

            // Read in ISO
            if (!File.Exists(isoPath))
            {
                Console.WriteLine("Invalid file path");
                return;
            }

            // Compare ISO against REDUMP_ISO_LENGTH to determine ISO type and XGD type
            bool? xiso = null;
            int? xgdType = null;

            FileInfo isoInfo = new(isoPath);
            long isoSize = isoInfo.Length;

            for (int i = 0; i < REDUMP_ISO_LENGTH.Length; i++)
            {
                if (REDUMP_ISO_LENGTH[i] == isoSize)
                {
                    xiso = false;
                    xgdType = i;
                    break;
                }
            }

            for (int i = 0; i < XISO_LENGTH.Length; i++)
            {
                if (XISO_LENGTH[i] == isoSize)
                {
                    xiso = true;
                    xgdType = i;
                    break;
                }
            }

            // Compare PVD against known PVDs to determine wave
            int? wave = null;
            if (xiso == false && xgdType == 4)
            {
                try
                {
                    using FileStream fs = new(isoPath, FileMode.Open, FileAccess.Read);

                    // Move the file stream position to the offset
                    long pvdOffset = 0x832E;
                    fs.Seek(pvdOffset, SeekOrigin.Begin);

                    // Read 16 bytes from the file starting at the specified offset
                    byte[] pvd = new byte[16];
                    int bytesRead = fs.Read(pvd, 0, pvd.Length);

                    if (bytesRead == 16)
                    {
                        string pvdString = Encoding.ASCII.GetString(pvd);


                        for (int i = 0; i < WAVE_PVD.Length; i++)
                        {
                            if (WAVE_PVD[i] == pvdString)
                            {
                                wave = i;
                                break;
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Failed to read PVD from {isoPath}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading PVD: {ex.Message}");
                }
            }

            // Extract video partition from redump ISO
            if (xiso == false && videoPath != null)
            {
                using FileStream isoFS = new(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using FileStream videoFS = new(videoPath, FileMode.Create, FileAccess.Write, FileShare.None);

                int videoType = xgdType switch
                {
                    0 => 0,
                    1 => 1,
                    2 => 2,
                    3 => 3,
                    4 => wave switch
                    {
                        0 => 1,
                        1 => 2,
                        2 => 3,
                        3 => 4,
                        4 or 5 or 6 or 7 => 5,
                        8 or 9 => 6,
                        10 or 11 or 12 => 7,
                        13 => 8,
                        14 or 15 => 9,
                        16 => 10,
                        17 or 18 => 11,
                        19 => 12,
                        20 => 13,
                        21 => 14,
                        _ => 16,
                    },
                    5 => 14,
                    6 => 15,
                    7 or _ => 16,
                };

                long l0Length = VIDEO_L0_LENGTH[videoType];
                long l1Length = VIDEO_L1_LENGTH[videoType];

                byte[] buf = new byte[64 * 2048];
                long numBytes = 0;

                while (numBytes < l0Length)
                {
                    int bytesRead = isoFS.Read(buf, 0, (int)Math.Min(buf.Length, l0Length - numBytes));

                    if (bytesRead == 0)
                        break;

                    videoFS.Write(buf, 0, bytesRead);
                    numBytes += bytesRead;
                }

                isoFS.Seek(isoSize - l1Length, SeekOrigin.Begin);

                numBytes = 0;
                while (numBytes < l1Length)
                {
                    int bytesRead = isoFS.Read(buf, 0, (int)Math.Min(buf.Length, l1Length - numBytes));

                    if (bytesRead == 0)
                        break;

                    videoFS.Write(buf, 0, bytesRead);
                    numBytes += bytesRead;
                }
            }

            // Extract game partition from redump ISO
            string dir = Path.GetDirectoryName(isoPath);
            string filename = Path.GetFileNameWithoutExtension(isoPath);
            string xisoPath = Path.Combine(dir, $"{filename}.xiso.iso");
            if (xiso == false && xisoPath != null)
            {
                using FileStream isoFS = new(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using FileStream xisoFS = new(xisoPath, FileMode.Create, FileAccess.Write, FileShare.None);

                int xisoType = xgdType switch
                {
                    0 => 0,
                    1 or 2 or 3 or 4 => 1,
                    5 => 2,
                    6 or 7 or _ => 3,
                };

                isoFS.Seek(XISO_OFFSET[xisoType], SeekOrigin.Begin);

                byte[] buf = new byte[1024 * 2048];
                long numBytes = 0;

                long xisoLength = XISO_LENGTH[xisoType];

                while (numBytes < xisoLength)
                {
                    int bytesRead = isoFS.Read(buf, 0, (int)Math.Min(buf.Length, xisoLength - numBytes));

                    if (bytesRead == 0)
                        break;

                    xisoFS.Write(buf, 0, bytesRead);
                    numBytes += bytesRead;
                }
            }
        }
    }
}
