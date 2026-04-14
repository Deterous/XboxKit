namespace XboxKit
{
    internal class XRD
    {
        // Create XRD file from redump ISO filestream
        public static long ExtractRebuildData(FileStream isoFS, FileStream xrdFS, int redumpIsoType)
        {
            // Write the magic bytes
            byte[] magic = [0x58, 0x52, 0x44, 0xFF];
            xrdFS.Write(magic, 0, magic.Length);

            // Write the version bytes
            byte[] version = [0x00, 0x01];
            xrdFS.Write(version, 0, version.Length);

            // Write the XGD type
            byte xgdType = redumpIsoType switch
            {
                0 => 1, // XGD1
                1 or 2 or 3 or 4 or 5 => 2, // XGD2
                6 or 7 => 3, // XGD3
                _ => 0xFF, // Unknown
            };
            xrdFS.Write(xgdType);

            // Write the XGD subtype (wave)
            if (xgdType == 2)
            {
                // Write XGD3 variant byte
                if (redumpIsoType == 5)
                    xrdFS.Write(0x80); // XGD2 / DVD-Video Hybrid
                else
                {
                    byte wave = (byte)GetWave(isoFS, redumpIsoType);
                    xrdFS.Write(wave);
                }
            }
            else if (xgdType == 3)
            {
                // Write XGD3 variant byte
                byte subType = redumpIsoType switch
                {
                    6 => 0, // XGD3 beta
                    7 => 1, // XGD3
                    _ => 0xFF, // Unknown
                };
                xrdFS.Write(subType);
            }
            else
            {
                // No sub-variants
                xrdFS.Write(0x00);
            }
        }
    }
}
