namespace XboxKit
{
    internal class XGD
    {
        // Wave Types:                            XGD2w0,             XGD2w1,             XGD2w2,             XGD2w3,             XGD2w4,             XGD2w5,             XGD2w6,             XGD2w7,             XGD2w8,             XGD2w9,            XGD2w10,            XGD2w11,            XGD2w12,            XGD2w13,            XGD2w14,            XGD2w15,            XGD2w16,            XGD2w17,            XGD2w18,            XGD2w19,            XGD2w20,           XGD2-Hybrid,           XGD1              XGD3-beta
        static readonly string[] WAVE_PVD = ["2004083110334900", "2005100712184600", "2006030621090700", "2009011416000000", "2009082417000000", "2009100517000000", "2009102917000000", "2010022116000000", "2010090417000000", "2010091517000000", "2010102817000000", "2011011816000000", "2011061217000000", "2011071217000000", "2011120716000000", "2012022116000000", "2012062117000000", "2012110716000000", "2012111816000000", "2013082617000000", "2015042617000000", "2006041012132800", "2001091310425500", "2010121616000000"];
        
        public static int GetVideoType(FileStream isoFS, int redumpIsoType)
        {
            int wave = GetWave(isoFS, redumpIsoType);

            // Determine size of output video ISO
            return redumpIsoType switch
            {
                0 => 0, // XGD1
                1 => 1, // XGD2 Wave 0
                2 => 2, // XGD2 Wave 1
                3 => 3, // XGD2 Wave 2
                4 => wave switch // XGD2 Wave 3-20
                {
                    0 => 1,                // E9B8ECFE
                    1 => 2,                // 739CEAB3
                    2 => 3,                // A4CFB59C
                    3 => 4,                // 2A4CCBD3
                    4 or 5 or 6 or 7 => 5, // 05C6C409
                    8 or 9 => 6,           // 0441D6A5
                    10 or 11 or 12 => 7,   // E18BC70B
                    13 => 8,               // 40DCB18F
                    14 or 15 => 9,         // 23A198FC
                    16 => 10,              // AB25DB47
                    17 or 18 => 11,        // 169EF597
                    19 => 12,              // 169EF597
                    20 => 13,              // 032CCF37
                    21 => 14,              // F48D24B8
                    22 => 0,               // 8FC52135
                    _ => -1,
                },
                5 => 14, // XGD2 (Hybrid)
                6 => wave switch // XGD3-beta or XGD3v0
                {
                    23 => 15, // XGD3-beta
                    _ => 16, // XGD3v0
                },
                7 => 17, // XGD3
                _ => -1,
            };
        }

        // Get XGD Wave
        public static int GetWave(FileStream isoFS, int redumpIsoType)
        {
            // Compare PVD creation datetime against known datetimes to determine wave
            int? wave = null;
            if (redumpIsoType == 4 || redumpIsoType == 6)
            {
                try
                {
                    isoFS.Seek(0x832D, SeekOrigin.Begin);
                    byte[] pvd = new byte[16];
                    int bytesRead = isoFS.Read(pvd, 0, pvd.Length);
                    if (bytesRead == 16)
                        wave = Array.IndexOf(WAVE_PVD, Encoding.ASCII.GetString(pvd));
                    else
                        return -1;
                }
                catch (Exception ex)
                {
                    return -1;
                }
            }
        }
    }
}
