using System;
using System.IO;
using System.Text;

namespace XboxKit
{
    internal class XRD
    {
        public static SabreTools.Data.Models.XRD.File? GetXRD(FileStream isoFS, SabreTools.Wrappers.XboxISO xboxISO, int redumpIsoType)
        {
            // Validate redumpIsoType
            if (redumpIsoType < 0 || redumpIsoType > 8)
                return null;

            SabreTools.Data.Models.XRD.File xrd = new();
            xrd.Magic = SabreTools.Data.Models.XRD.Constants.MagicBytes;

            // Set XGD Type
            xrd.XGDType = redumpIsoType switch
            {
                0 or 1 => 0, // XGD1
                2 or 3 or 4 or 5 or 6 => 1, // XGD2
                7 or 8 => 3, // XGD3
                _ => 0,
            };

            // Set XGD Subtype
            if (xrd.XGDType == 1)
            {
                xrd.XGDSubtype = redumpIsoType switch
                {
                    0 => 0, // XGD1 Beta (XB00104M)
                    1 => 1, // Standard XGD1
                    _ => 0xFF, // Unknown XGD1 Subtype
                };
            }
            else if (xrd.XGDType == 2)
            {
                var wave = GetWave(xboxISO.VideoPartition);
                xrd.XGDSubtype = wave switch
                {
                    >= 0 and <= 20 => (byte)wave, // XGD2 Wave 0-20
                    21 => 0x81, // XGD2-Hybrid
                    _ => 0xFF, // Unknown
                };
            }
            else if (xrd.XGDType == 3)
            {
                if (redumpIsoType == 7)
                {
                    var wave = GetWave(xboxISO.VideoPartition);
                    if (wave == 23)
                        xrd.XGDSubtype = 0x80; // HCXGD2 Internal Beta (FD91511A)
                    else
                        xrd.XGDSubtype = 0; // XGD3v0 (152C2978, FFFFFDEB, FFFFFDE3)
                }
                else if (redumpIsoType == 8)
                    xrd.XGDSubtype = 1; // Standard XGD3
                else
                    xrd.XGDSubtype = 0xFF; // Unknown
            }

            // Set Version field, use Version 2 for non-standard Video partitions
            bool nonStandardVideo = xrd.XGDType == 3 || xrd.XGDSubtype == 0xFF;
            if (nonStandardVideo)
                xrd.Version = 2;
            else
                xrd.Version = 1;

            // Set Ringcode
            if (xrd.XGDType == 1)
                xrd.Ringcode = [0, 0, 0, 0, 0, 0, 0, 0]; // GetXboxRingcode();
            else if (xrd.XGDType == 2 || xrd.XGDType == 3)
                xrd.Ringcode = [0, 0, 0, 0, 0, 0, 0, 0]; // GetXbox360Ringcode();
            
            // Set redump ISO size/hashes
            xrd.RedumpSize = 0;
            xrd.RedumpCRC = [0, 0, 0, 0];
            xrd.RedumpMD5 = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            xrd.RedumpSHA1 = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            xrd.RawXISOSize = 0;
            xrd.RawXISOCRC = [0, 0, 0, 0];
            xrd.RawXISOMD5 = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            xrd.RawXISOSHA1 = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            xrd.CookedXISOSize = 0;
            xrd.CookedXISOCRC = [0, 0, 0, 0];
            xrd.CookedXISOMD5 = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            xrd.CookedXISOSHA1 = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            xrd.VideoISOSize = 0;
            xrd.VideoISOCRC = [0, 0, 0, 0];
            xrd.VideoISOMD5 = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            xrd.VideoISOSHA1 = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            if (xrd.Version == 2)
            {
                xrd.WipedVideoISOSize = 0;
                xrd.WipedVideoISOCRC = [0, 0, 0, 0];
                xrd.WipedVideoISOMD5 = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
                xrd.WipedVideoISOSHA1 = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            }
            xrd.FillerSize = 0;
            xrd.FillerCRC = [0, 0, 0, 0];
            xrd.FillerMD5 = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            xrd.FillerSHA1 = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

            // Set security sector values
            if (xrd.XGDType == 1)
            {
                xrd.SecuritySectors = new uint[16];
            }
            else if (xrd.XGDType == 2 || xrd.XGDType == 3)
            {
                xrd.SecuritySectors = new uint[2];
            }

            // Set Xbox Certificate structure
            if (xrd.XGDType == 1)
            {
                // TODO: Parse XBE Certificate
                xrd.XboxCertificate = new();
            }
            else if (xrd.XGDType == 2 || xrd.XGDType == 3)
            {
                // TODO: Parse XEX Certificate
                xrd.Xbox360Certificate = new();
            }

            // Set XDVDFS fields
            // TODO: Calculate all file hashes
            xrd.FileCount = 0;
            xrd.FileInfo = new SabreTools.Data.Models.XRD.FileEntry[0];
            xrd.VolumeDescriptor = xboxISO.GamePartition.VolumeDescriptor;
            xrd.LayoutDescriptor = xboxISO.GamePartition.LayoutDescriptor;
            xrd.DirectoryCount = 0;
            xrd.DirectoryInfo = new SabreTools.Data.Models.XRD.DirectoryEntry[0];

            if (xrd.Version == 2)
            {
                xrd.VideoISOFileCount = 0;
                xrd.VideoISOFileInfo = new SabreTools.Data.Models.XRD.FileEntry[0];
            }

            xrd.XRDSize = 0;
            xrd.XRDSHA1 = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

            return xrd;
        }

        public static int? GetWave(SabreTools.Data.Models.ISO9660.Volume volume)
        {
            var vd = volume.VolumeDescriptorSet[0];
            if (vd is not SabreTools.Data.Models.ISO9660.PrimaryVolumeDescriptor pvd)
                return null;
            var pvdDatetime = new byte[16];
            Buffer.BlockCopy(pvd.VolumeCreationDateTime.Year, 0, pvdDatetime, 0,  4);
            Buffer.BlockCopy(pvd.VolumeCreationDateTime.Month, 0, pvdDatetime, 4,  2);
            Buffer.BlockCopy(pvd.VolumeCreationDateTime.Day, 0, pvdDatetime, 6,  2);
            Buffer.BlockCopy(pvd.VolumeCreationDateTime.Hour, 0, pvdDatetime, 8,  2);
            Buffer.BlockCopy(pvd.VolumeCreationDateTime.Minute, 0, pvdDatetime, 10,  2);
            Buffer.BlockCopy(pvd.VolumeCreationDateTime.Second, 0, pvdDatetime, 12,  2);
            Buffer.BlockCopy(pvd.VolumeCreationDateTime.Centisecond, 0, pvdDatetime, 14,  2);
            return Array.IndexOf(XGD.WAVE_PVD, Encoding.ASCII.GetString(pvdDatetime));
        }

        // Xbox disc ringcode is the Media ID, can be determined from certificate
        private static byte[] GetXboxRingcode(FileStream isoFS)
        {
            //long xisoOffset = Program.XISO_OFFSET[0];

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
            long xisoOffset = redumpIsoType switch
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
            return Encoding.ASCII.GetBytes(BitConverter.ToString(mediaID).Replace("-", ""));
        }
    }
}
