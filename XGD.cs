using System;
using System.IO;
using System.Text;

namespace XboxKit
{
    internal class XGD
    {
        // Wave Types:                            XGD2w0,             XGD2w1,             XGD2w2,             XGD2w3,             XGD2w4,             XGD2w5,             XGD2w6,             XGD2w7,             XGD2w8,             XGD2w9,            XGD2w10,            XGD2w11,            XGD2w12,            XGD2w13,            XGD2w14,            XGD2w15,            XGD2w16,            XGD2w17,            XGD2w18,            XGD2w19,            XGD2w20,           XGD2-Hybrid,           XGD1              XGD3-beta
        internal static readonly string[] WAVE_PVD = ["2004083110334900", "2005100712184600", "2006030621090700", "2009011416000000", "2009082417000000", "2009100517000000", "2009102917000000", "2010022116000000", "2010090417000000", "2010091517000000", "2010102817000000", "2011011816000000", "2011061217000000", "2011071217000000", "2011120716000000", "2012022116000000", "2012062117000000", "2012110716000000", "2012111816000000", "2013082617000000", "2015042617000000", "2006041012132800", "2001091310425500", "2010121616000000"];
        
        public static int GetVideoType(FileStream? isoFS, int redumpIsoType)
        {
            int wave = -1;
            if (redumpIsoType == 5 || redumpIsoType == 7)
                wave = GetWave(isoFS, redumpIsoType);

            // Determine size of output video ISO
            return redumpIsoType switch
            {
                0 => 0, // XGD1-Beta
                1 => 1, // XGD1
                2 => 2, // XGD2 Wave 0
                3 => 3, // XGD2 Wave 1
                4 => 4, // XGD2 Wave 2
                5 => wave switch // XGD2 Wave 3-20
                {
                    0 => 2,                // E9B8ECFE
                    1 => 3,                // 739CEAB3
                    2 => 4,                // A4CFB59C
                    3 => 5,                // 2A4CCBD3
                    4 or 5 or 6 or 7 => 6, // 05C6C409
                    8 or 9 => 7,           // 0441D6A5
                    10 or 11 or 12 => 8,   // E18BC70B
                    13 => 9,               // 40DCB18F
                    14 or 15 => 10,        // 23A198FC
                    16 => 11,              // AB25DB47
                    17 or 18 => 12,        // 169EF597
                    19 => 13,              // 169EF597
                    20 => 14,              // 032CCF37
                    21 => 15,              // F48D24B8
                    22 => 0,               // 8FC52135
                    _ => -1,
                },
                6 => 14, // XGD2-Hybrid
                7 => wave switch // XGD3-Beta or XGD3v0
                {
                    23 => 15, // D92C9096 (XGD3-Beta)
                    _ => 16,  // E1647069 (XGD3v0)
                },
                8 => 17, // XGD3
                _ => -1,
            };
        }

        // Get XGD Wave
        public static int GetWave(FileStream? isoFS, int redumpIsoType)
        {
            if (isoFS is null)
                return -1;

            // Compare PVD creation datetime against known datetimes to determine wave
            if (redumpIsoType == 4 || redumpIsoType == 6)
            {
                try
                {
                    isoFS.Seek(0x832D, SeekOrigin.Begin);
                    byte[] pvd = new byte[16];
                    int bytesRead = isoFS.Read(pvd, 0, pvd.Length);
                    if (bytesRead == 16)
                        return Array.IndexOf(WAVE_PVD, Encoding.ASCII.GetString(pvd));
                }
                catch { }
            }
            
            return -1;
        }
    }
}
