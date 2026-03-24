using System;
using System.IO;

namespace XboxKit
{
    internal class Utils
    {
        // Read uint16 from filestream
        public static ushort ReadUShort(FileStream fs)
        {
            byte[] buffer = new byte[2];
            if (fs.Read(buffer, 0, 2) != 2)
                throw new EndOfStreamException("[ERROR] Failed to read UShort");
            return BitConverter.ToUInt16(buffer, 0);
        }

        // Read uint32 from filestream
        public static uint ReadUInt(FileStream fs)
        {
            byte[] buffer = new byte[4];
            if (fs.Read(buffer, 0, 4) != 4)
                throw new EndOfStreamException("[ERROR] Failed to read UInt32");
            return BitConverter.ToUInt32(buffer, 0);
        }

        // Ensure proper writing to byte array
        public static bool WriteBytes(FileStream fs, byte[] outBA, long offset)
        {
            long numBytes = 0;
            if (offset >= 0)
                fs.Seek(offset, SeekOrigin.Begin);
            while (numBytes < outBA.Length)
            {
                int bytesRead = fs.Read(outBA, 0, (int)(outBA.Length - numBytes));
                if (bytesRead == 0)
                    break;

                numBytes += bytesRead;
            }
            return numBytes == outBA.Length;
        }

        // Ensure proper writing to filestream
        public static bool WriteBytes(FileStream inFS, FileStream outFS, long offset, long length)
        {
            byte[] buf = new byte[64 * SECTOR_SIZE];
            long numBytes = 0;
            if (offset >= 0)
                inFS.Seek(offset, SeekOrigin.Begin);
            while (numBytes < length)
            {
                int bytesRead = inFS.Read(buf, 0, (int)Math.Min(buf.Length, length - numBytes));
                if (bytesRead == 0)
                    break;

                outFS.Write(buf, 0, bytesRead);
                numBytes += bytesRead;
            }
            return numBytes == length;
        }

        // Write zeroes to filestream
        public static void WriteZeroes(FileStream outFS, long offset, long length)
        {
            byte[] buf = new byte[64 * SECTOR_SIZE];
            long numBytes = 0;
            if (offset >= 0)
                outFS.Seek(offset, SeekOrigin.Begin);
            while (numBytes < length)
            {
                int bytesToWrite = (int)Math.Min(buf.Length, length - numBytes);
                outFS.Write(buf, 0, bytesToWrite);
                numBytes += bytesToWrite;
            }
            return;
        }
    }
}
