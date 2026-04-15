using System;
using System.IO;
using System.Text;

namespace XboxKit
{
    internal class XRD
    {
        // Create XRD file from redump ISO filestream
        public static void ExtractRebuildData(FileStream isoFS, FileStream xrdFS, int redumpIsoType)
        {
            // Write the magic and version bytes (offset 0x00-0x05)
            byte[] magic = [0x58, 0x52, 0x44, 0xFF, 0x00, 0x01];
            xrdFS.Write(magic, 0, magic.Length);

            // Write the XGD type (offset 0x06)
            byte xgdType = redumpIsoType switch
            {
                0 => 1, // XGD1
                1 or 2 or 3 or 4 or 5 => 2, // XGD2
                6 or 7 => 3, // XGD3
                _ => 0xFF, // Unknown
            };
            xrdFS.WriteByte(xgdType);

            // Write the XGD subtype/wave (offset 0x07)
            byte subType = 0xFF; // Default: "Unknown subtype"
            int wave = XGD.GetWave(isoFS, redumpIsoType);
            if (xgdType == 1)
            {
                if (wave == 22)
                    subType = 0x01; // Standard XGD1
                else
                    subType = 0xFF; // Unknown XGD1
            }
            else if (xgdType == 2)
            {
                if (wave > 20 || wave < 0)
                    wave = 0xFF;
                subType = redumpIsoType switch
                {
                    1 or 2 or 3 or 4 => (byte)wave, // XGD2 wave 0-20
                    5 => 0x80, // XGD2 / DVD-Video Hybrid
                    _ => 0xFF, // Unknown subtype
                };
            }
            else if (xgdType == 3)
            {
                subType = redumpIsoType switch
                {
                    6 => 0, // XGD3 Beta
                    7 => 1, // Standard XGD3
                    _ => 0xFF, // Unknown subtype
                };
            }
            xrdFS.WriteByte(subType);

            // 8-character ringcode ASCII (offset 0x08-0x0F)
            if (xgdType == 1)
            {
                byte[] ringcode = GetXboxRingcode(isoFS);
                xrdFS.Write(ringcode);
            }
            else if (xgdType == 2 || xgdType == 3)
            {
                byte[] ringcode = GetXbox360Ringcode(isoFS, redumpIsoType);
                xrdFS.Write(ringcode);
            }
            else
            {
                // Unknown XGD, zeroed bytes
                byte[] reserved = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
                xrdFS.Write(reserved);
            }
        }

        // Xbox disc ringcode is the Media ID, can be determined from certificate
        private static byte[] GetXboxRingcode(FileStream isoFS)
        {
            int xisoOffset = Program.XISO_OFFSET[0];

            // TODO: Find XBE file, get cert offset (cert - base), read TitleID/Region/Version
            byte[] titleID = [0x07, 0x00, 0x4E, 0x4B]; // cert offset + 0x08
            byte[] regions = [0x01, 0x00, 0x00, 0x00]; // cert offset + 0xA0
            byte[] version = [0x06, 0x00, 0x00, 0x00]; // cert offset + 0xB0
            byte[] ringcode = new byte[8];
            ringcode[0] = titleID[3];
            ringcode[1] = titleID[2];
            ushort idNum = (ushort)((titleID[1] << 8) | titleID[0]);
            string idStr = idNum.ToString("D3");
            for (int i = 0; i < 3; i++)
                ringcode[2 + i] = (byte)idStr[i];
            
            byte[] ver = Encoding.ASCII.GetBytes(version[0].ToString("D2"));
            ringcode[5] = ver[0];
            ringcode[6] = ver[1];

            ringcode[7] = regions[0] switch
            {
                1 => (byte)'A',
                2 => (byte)'J',
                4 => (byte)'E',
                _ => (byte)'?'
            };

            return ringcode;
        }

        // Xbox 360 disc ringcode is the last 4 bytes of Media ID
        public static byte[] GetXbox360Ringcode(FileStream isoFS, int redumpIsoType)
        {
            int xisoOffset = redumpIsoType switch
            {
                1 or 2 or 3 or 4 => Program.XISO_OFFSET[1], // XGD2 wave 0-20
                5 => Program.XISO_OFFSET[2], // XGD2 / DVD-Video Hybrid
                6 or 7 => Program.XISO_OFFSET[3], // XGD3
                _ => -1, // Unknown
            };
            if (xisoOffset == -1)
                return [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

            isoFS.Seek(xisoOffset + XDVDFS.XISO_HEADER_OFFSET + 20, SeekOrigin.Begin);
            uint rootOffset = Utils.ReadUInt(isoFS);
            long dirOffset = (long)rootOffset * XDVDFS.SECTOR_SIZE + xisoOffset;
            uint rootSize = Utils.ReadUInt(isoFS);
 
            byte[] mediaID = [0x63, 0xC4, 0x41, 0x62];
            // TODO: Find XEX file, get cert offset, read 4 bytes at offset 0x14C
            byte[] ringcode = Encoding.ASCII.GetBytes(BitConverter.ToString(mediaID).Replace("-", ""));
        }
    }
}
