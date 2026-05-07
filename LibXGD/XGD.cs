using System;
using System.IO;
using System.Text;

namespace LibXGD
{
    public class XGD
    {
        // XISO Types:                                     XGD1,    XGD2,   XGD2-Hybrid,    XGD3
        public static readonly long[] XISO_OFFSET = [0x18300000, 0x0FD90000, 0x89D80000, 0x02080000];
        public static readonly long[] XISO_LENGTH = [0x1A2DB0000, 0x1B3880000, 0x0BF8A0000, 0x204510000];
        // Redump ISO Types:                                 XGD1-Beta,        XGD1,      XGD2w0,      XGD2w1,      XGD2w2,     XGD2w3+, XGD2-Hybrid,      XGD3v0,        XGD3
        public static readonly long[] REDUMP_ISO_LENGTH = [0x1D330C000, 0x1D26A8000, 0x1D3301800, 0x1D2FEF800, 0x1D3082000, 0x1D3390000, 0x1D31A0000, 0x208E05800, 0x208E03800];
        // Video Partition Types:                        XGD1-Beta,      XGD1,  XGD2w0,   XGD2w1,   XGD2w2,    XGD2w3,  XGD2w4-7,  XGD2w8-9, XGD2w10-12,  XGD2w13, XGD2w14-15,  XGD2w16, XGD2w17-18,  XGD2w19,   XGD2w20, XGD2-Hybrid, XGD3-Beta,   XGD3v0,      XGD3
        public static readonly long[] VIDEO_L0_LENGTH = [0x7458000, 0x0D58000, 0xA8000, 0x548000, 0x438000, 0x4BB0000, 0x56C0000, 0x5460000, 0x5BA0000, 0x5C10000, 0x55D0000, 0x55C0000, 0x8A40000, 0x8A90000, 0x8E80000, 0x4B1D0000, 0x1878000, 0x1880000, 0x1880000];
        public static readonly long[] VIDEO_L1_LENGTH = [0x73B4000, 0x0050000, 0x09800, 0x197800, 0x11A000, 0x4BA0000, 0x56B0000, 0x5450000, 0x5B90000, 0x5C00000, 0x55C0000, 0x55B0000, 0x8A30000, 0x8A80000, 0x8E70000, 0x4AFD0000, 0x186D800, 0x1875800, 0x1873800];
        public static readonly long[] VIDEO_LENGTH = [0xE80C000, 0x0DA8000, 0xB1800, 0x6DF800, 0x552000, 0x9750000, 0xAD70000, 0xA8B0000, 0xB730000, 0xB810000, 0xAB90000, 0xAB70000, 0x11470000, 0x11510000, 0x11CF0000, 0x961A0000, 0x30E5800, 0x30F5800, 0x30F3800];
        // Wave Types:                                      XGD2w0,             XGD2w1,             XGD2w2,             XGD2w3,             XGD2w4,             XGD2w5,             XGD2w6,             XGD2w7,             XGD2w8,             XGD2w9,            XGD2w10,            XGD2w11,            XGD2w12,            XGD2w13,            XGD2w14,            XGD2w15,            XGD2w16,            XGD2w17,            XGD2w18,            XGD2w19,            XGD2w20,           XGD2-Hybrid,           XGD1           XGD3-beta
        public static readonly string[] WAVE_PVD = ["2004083110334900", "2005100712184600", "2006030621090700", "2009011416000000", "2009082417000000", "2009100517000000", "2009102917000000", "2010022116000000", "2010090417000000", "2010091517000000", "2010102817000000", "2011011816000000", "2011061217000000", "2011071217000000", "2011120716000000", "2012022116000000", "2012062117000000", "2012110716000000", "2012111816000000", "2013082617000000", "2015042617000000", "2006041012132800", "2001091310425500", "2010121616000000"];
        
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
