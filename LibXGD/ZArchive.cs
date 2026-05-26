using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Nanook.GrindCore;

namespace LibXGD
{
    public class ZArchive
    {
        private const int BLOCK_SIZE = 64 * 1024;
        private const int BLOCKS_PER_RECORD = 16;
        private static readonly byte[] MAGIC = [0x16, 0x9F, 0x52, 0xD6];
        private static readonly byte[] VERSION1 = [0x61, 0xBF, 0x3A, 0x01];

        private class PathNode
        {
            public List<PathNode> Subnodes = [];
            public bool IsFile;
            public int NameIndex;
            public long SourceOffset;
            public ulong FileSize;
            public ulong FileOffset;
            public uint NodeStartIndex;
        }

        // Filestream wrapper that SHA-256 hashes all written bytes
        private class HashingStream(FileStream fs)
        {
            private readonly FileStream _fs = fs;
            private readonly SHA256 _sha = SHA256.Create();
            private readonly byte[] _buf = new byte[8];
            public long Position;

            public void Write(byte[] buf, int offset, int count)
            {
                _fs.Write(buf, offset, count);
                _sha.TransformBlock(buf, offset, count, null, 0);
                Position += count;
            }

            public void Write(byte b)
            {
                _buf[0] = b;
                Write(_buf, 0, 1);
            }

            public void Write(ushort v)
            {
                _buf[0] = (byte)(v >> 8);
                _buf[1] = (byte)v;
                Write(_buf, 0, 2);
            }

            public void Write(uint v)
            {
                _buf[0] = (byte)(v >> 24);
                _buf[1] = (byte)(v >> 16);
                _buf[2] = (byte)(v >> 8); 
                _buf[3] = (byte)v;
                Write(_buf, 0, 4);
            }

            public void Write(ulong v)
            {
                _buf[0] = (byte)(v >> 56);
                _buf[1] = (byte)(v >> 48);
                _buf[2] = (byte)(v >> 40);
                _buf[3] = (byte)(v >> 32);
                _buf[4] = (byte)(v >> 24);
                _buf[5] = (byte)(v >> 16);
                _buf[6] = (byte)(v >> 8); 
                _buf[7] = (byte)v;
                Write(_buf, 0, 8);
            }

            public byte[] FinalizeHash(byte[] lastBlock)
            {
                _sha.TransformFinalBlock(lastBlock, 0, lastBlock.Length);
                byte[] hash = _sha.Hash!;
                _sha.Dispose();
                return hash;
            }
        }

        private static int GetOrAddName(List<string> names, Dictionary<string, int> nameLookup, string name)
        {
            if (nameLookup.TryGetValue(name, out int index))
                return index;
            index = names.Count;
            names.Add(name);
            nameLookup[name] = index;
            return index;
        }

        // Case-insensitive name comparison matching ZArchive canonical ordering
        private static int CompareNodeName(string n1, string n2)
        {
            int minLen = Math.Min(n1.Length, n2.Length);
            for (int i = 0; i < minLen; i++)
            {
                char c1 = n1[i];
                char c2 = n2[i];
                if (c1 >= 'A' && c1 <= 'Z')
                    c1 = (char)(c1 + ('a' - 'A'));
                if (c2 >= 'A' && c2 <= 'Z')
                    c2 = (char)(c2 + ('a' - 'A'));
                if (c1 != c2)
                    return (int)(byte)c1 - (int)(byte)c2;
            }
            return n1.Length.CompareTo(n2.Length);
        }

        // Create ZArchive from game files in an XISO
        public static bool CreateZAR(FileStream isoFS, long xisoOffset, string zarPath, bool removeUpdate, bool quiet)
        {
            // Parse XDVDFS volume descriptor to get root directory
            long headerOffset = xisoOffset + XDVDFS.XISO_HEADER_OFFSET;
            isoFS.Seek(headerOffset + 20, SeekOrigin.Begin);
            uint rootOffset = Utils.ReadUInt(isoFS);
            uint rootSize = Utils.ReadUInt(isoFS);

            // Build path tree from XDVDFS
            var names = new List<string>();
            var nameLookup = new Dictionary<string, int>();
            var rootNode = new PathNode { NameIndex = GetOrAddName(names, nameLookup, "") };
            ParseXDVDFS(isoFS, xisoOffset, (long)rootOffset * XDVDFS.SECTOR_SIZE, rootSize, 0, rootNode, names, nameLookup);

            // Optionally exclude system update from ZAR
            if (removeUpdate)
                rootNode.Subnodes.RemoveAll(n => !n.IsFile && names[n.NameIndex] == "$SystemUpdate");
            
            // Sort root directory entries (case-insensitive)
            rootNode.Subnodes.Sort((a, b) => CompareNodeName(names[a.NameIndex], names[b.NameIndex]));

            // Create ZAR file
            if (!quiet) Console.WriteLine($"[INFO] Writing ZArchive to {zarPath}");
            using FileStream zarFS = new(zarPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            var hs = new HashingStream(zarFS);

            // Write Zstd compressed data
            if (!WriteCompressedData(isoFS, xisoOffset, hs, rootNode, out var offsetRecords))
                return false;
            ulong compressedDataEnd = (ulong)hs.Position;

            // Pad to 8-byte alignment with 0x00
            while (hs.Position % 8 != 0)
                hs.Write((byte)0);

            // Write offset records, keep track of location within ZAR
            ulong offsetRecordsStart = (ulong)hs.Position;
            WriteOffsetRecords(hs, offsetRecords);

            // Write offset records, keep track of location within ZAR
            ulong nameTableStart = (ulong)hs.Position;
            WriteNameTable(hs, names, out var nameOffsets);

            // Write offset records, keep track of location within ZAR
            ulong fileTreeStart = (ulong)hs.Position;
            WriteFileTree(hs, rootNode, nameOffsets);

            // Write footer, using the location of each section
            WriteFooter(zarFS, hs, compressedDataEnd, offsetRecordsStart, nameTableStart, fileTreeStart);
            return true;
        }

        // Recursively build path tree from XDVDFS directory structure
        private static void ParseXDVDFS(FileStream isoFS, long isoOffset, long dirOffset, uint dirSize, long childOffset, PathNode parentNode, List<string> names, Dictionary<string, int> nameLookup)
        {
            if (childOffset >= dirSize)
                return;

            // Read XDVDFS directory entry
            long pos = isoOffset + dirOffset + childOffset;
            isoFS.Seek(pos, SeekOrigin.Begin);

            ushort leftChild = Utils.ReadUShort(isoFS);
            ushort rightChild = Utils.ReadUShort(isoFS);
            uint entrySector = Utils.ReadUInt(isoFS);
            uint entrySize = Utils.ReadUInt(isoFS);
            byte attributes = (byte)isoFS.ReadByte();
            byte nameLength = (byte)isoFS.ReadByte();
            byte[] nameBytes = new byte[nameLength];
            if (isoFS.Read(nameBytes, 0, nameLength) != nameLength)
                return;

            string name = Encoding.ASCII.GetString(nameBytes);
            bool isDirectory = (attributes & 0x10) != 0;
            long entryOffset = (long)entrySector * XDVDFS.SECTOR_SIZE;

            // Traverse left subtree
            if (leftChild != 0 && leftChild != 0xFFFF)
                ParseXDVDFS(isoFS, isoOffset, dirOffset, dirSize, (long)leftChild * 4, parentNode, names, nameLookup);

            // Create node for current entry
            int nameIndex = GetOrAddName(names, nameLookup, name);
            var node = new PathNode { IsFile = !isDirectory, NameIndex = nameIndex };

            if (isDirectory)
            {
                // Recurse into subdirectory and sort its children
                ParseXDVDFS(isoFS, isoOffset, entryOffset, entrySize, 0, node, names, nameLookup);
                node.Subnodes.Sort((a, b) => CompareNodeName(names[a.NameIndex], names[b.NameIndex]));
            }
            else
            {
                node.SourceOffset = entryOffset;
                node.FileSize = entrySize;
            }

            parentNode.Subnodes.Add(node);

            // Traverse right subtree
            if (rightChild != 0 && rightChild != 0xFFFF)
                ParseXDVDFS(isoFS, isoOffset, dirOffset, dirSize, (long)rightChild * 4, parentNode, names, nameLookup);
        }

        // Write all file data as Zstd-compressed 64KB blocks
        private static bool WriteCompressedData(FileStream isoFS, long xisoOffset, HashingStream hs, PathNode rootNode, out List<(ulong BaseOffset, ushort[] Sizes)> offsetRecords)
        {
            // Offset record state
            offsetRecords = [];
            ushort[] sizes = new ushort[BLOCKS_PER_RECORD];
            int count = 0;
            ulong recordBase = 0;

            // File reading state
            byte[] buf = new byte[BLOCK_SIZE];
            int bufPos = 0;
            ulong inputOffset = 0;

            // Parse directory nodes
            var queue = new Queue<PathNode>([rootNode]);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();

                // If node is a directory, traverse in BFS order
                if (!node.IsFile)
                {
                    foreach (var child in node.Subnodes)
                        queue.Enqueue(child);

                    continue;
                }

                // Compress file data in 64KB blocks
                node.FileOffset = inputOffset;
                isoFS.Seek(xisoOffset + node.SourceOffset, SeekOrigin.Begin);
                long remaining = (long)node.FileSize;

                while (remaining > 0)
                {
                    // Fill buffer (64KiB)
                    int toRead = (int)Math.Min(BLOCK_SIZE - bufPos, remaining);
                    int bytesRead = isoFS.Read(buf, bufPos, toRead);
                    if (bytesRead == 0)
                        return false;

                    bufPos += bytesRead;
                    remaining -= bytesRead;
                    inputOffset += (ulong)bytesRead;

                    // Flush full block
                    if (bufPos == BLOCK_SIZE)
                    {
                        FlushBlock(hs, buf, offsetRecords, ref sizes, ref count, ref recordBase);
                        bufPos = 0;
                    }
                }
            }

            // Flush final partial block
            if (bufPos > 0)
            {
                // Zero-pad up to block size
                Array.Clear(buf, bufPos, BLOCK_SIZE - bufPos);
                FlushBlock(hs, buf, offsetRecords, ref sizes, ref count, ref recordBase);
            }

            // Append remaining offset record
            if (count > 0)
                offsetRecords.Add((recordBase, sizes));

            return true;
        }

        // Compress and write a single 64KB block, tracking offset records
        private static void FlushBlock(HashingStream hs, byte[] data, List<(ulong BaseOffset, ushort[] Sizes)> offsetRecords, ref ushort[] sizes, ref int count, ref ulong recordBase)
        {
            if (count == BLOCKS_PER_RECORD)
            {
                offsetRecords.Add((recordBase, sizes));
                sizes = new ushort[BLOCKS_PER_RECORD];
                count = 0;
            }

            if (count == 0)
                recordBase = (ulong)hs.Position;

            // Compress with Zstd (level 6 to match canonical C++ implementation)
            byte[] compressed = new byte[BLOCK_SIZE + 384];
            int compressedSize = BLOCK_SIZE;
            using (var compressor = CompressionBlockFactory.Create(
                CompressionAlgorithm.ZStd,
                new CompressionOptions
                {
                    BlockSize = BLOCK_SIZE,
                    Type = (CompressionType)6
                }))
                compressor.Compress(data, 0, BLOCK_SIZE, compressed, 0, ref compressedSize);

            bool useRaw = compressedSize >= BLOCK_SIZE || compressedSize < 0;
            byte[] toWrite = useRaw ? data : compressed;
            int storedSize = useRaw ? BLOCK_SIZE : compressedSize;

            hs.Write(toWrite, 0, storedSize);
            sizes[count++] = (ushort)(storedSize - 1);
        }

        // Write offset records section
        private static void WriteOffsetRecords(HashingStream hs, List<(ulong BaseOffset, ushort[] Sizes)> records)
        {
            foreach (var (baseOffset, sizes) in records)
            {
                hs.Write(baseOffset);
                foreach (ushort s in sizes)
                    hs.Write(s);
            }
        }

        // Write name table section, Length-prefixed UTF-8 name strings
        // Also keep track of the name offsets
        private static void WriteNameTable(HashingStream hs, List<string> names, out uint[] nameOffsets)
        {
            nameOffsets = new uint[names.Count];
            uint pos = 0;
            for (int i = 0; i < names.Count; i++)
            {
                nameOffsets[i] = pos;
                byte[] nameBytes = Encoding.UTF8.GetBytes(names[i]);
                int len = nameBytes.Length;

                // Write length prefix
                if (len >= 0x80)
                {
                    // 2-byte length prefix for long filenames
                    byte[] header = [(byte)((len & 0x7F) | 0x80), (byte)(len >> 7)];
                    hs.Write(header, 0, 2);
                    pos += 2;
                }
                else
                {
                    // One byte length prefix for small filenames
                    hs.Write([(byte)(len & 0x7F)], 0, 1);
                    pos += 1;
                }

                // Write UTF-8 name
                hs.Write(nameBytes, 0, nameBytes.Length);
                pos += (uint)nameBytes.Length;
            }
        }

        // Write file tree section (BFS order)
        private static void WriteFileTree(HashingStream hs, PathNode rootNode, uint[] nameOffsets)
        {
            // Assign NodeStartIndex for the directories
            var nodes = new List<PathNode>();
            var queue = new Queue<PathNode>([rootNode]);
            uint idx = 1; // First NodeStartIndex is 1
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                nodes.Add(node);
                if (node.IsFile)
                    continue;

                // Assign BFS index range for this directory's children
                node.NodeStartIndex = idx;
                idx += (uint)node.Subnodes.Count;
                foreach (var child in node.Subnodes)
                    queue.Enqueue(child);
            }

            // Serialize file tree nodes
            foreach (var node in nodes)
            {
                // Type/name offset flag
                if (node == rootNode)
                    hs.Write((uint)0x7FFFFFFF);
                else if (node.IsFile)
                    hs.Write(0x80000000 | nameOffsets[node.NameIndex]);
                else
                    hs.Write(nameOffsets[node.NameIndex]);

                if (node.IsFile)
                {
                    // File record
                    hs.Write((uint)(node.FileOffset & 0xFFFFFFFF)); // File offset low 32 bits
                    hs.Write((uint)(node.FileSize & 0xFFFFFFFF)); // File size, low 32 bits
                    hs.Write((ushort)(node.FileSize >> 32)); // File size high 16 bits
                    hs.Write((ushort)(node.FileOffset >> 32)); // File offset high 16 bits
                }
                else
                {
                    // Directory record
                    hs.Write(node.NodeStartIndex); // First child index
                    hs.Write((uint)node.Subnodes.Count); // Child count
                    hs.Write((uint)0); // Reserved
                }
            }
        }

        private static void WriteFooter(FileStream zarFS, HashingStream hs, ulong compressedDataSize, ulong offsetRecordsStart, ulong nameTableStart, ulong fileTreeStart)
        {
            ulong end = (ulong)hs.Position;
            ulong totalSize = end + 144;

            using var ms = new MemoryStream(144);
            using var bw = new BinaryWriter(ms);
            WriteBE(bw, (ulong)0); // Location of compressed data
            WriteBE(bw, compressedDataSize); // Size of compressed data
            WriteBE(bw, offsetRecordsStart); // Location of offset records
            WriteBE(bw, nameTableStart - offsetRecordsStart); // Size of offset records
            WriteBE(bw, nameTableStart); // Location of name table
            WriteBE(bw, fileTreeStart - nameTableStart); // Size of name table
            WriteBE(bw, fileTreeStart); // Location of file tree
            WriteBE(bw, end - fileTreeStart); // Size of file tree
            WriteBE(bw, end); // Location of meta directory
            WriteBE(bw, (ulong)0); // Size of meta directory
            WriteBE(bw, end); // Location of meta data
            WriteBE(bw, (ulong)0); // Size of meta data
            bw.Write(new byte[32]); // SHA-256 hash
            WriteBE(bw, totalSize); // Total size of ZAR file
            bw.Write(VERSION1); // Version bytes / extended magic bytes
            bw.Write(MAGIC); // Magic bytes

            byte[] footerBytes = ms.ToArray();
            byte[] hash = hs.FinalizeHash(footerBytes);
            Array.Copy(hash, 0, footerBytes, 96, 32); // Overwrite with the calculated hash
            zarFS.Write(footerBytes, 0, footerBytes.Length);
        }

        // Big-endian write helper for BinaryWriter (footer construction only)
        private static void WriteBE(BinaryWriter bw, ulong v) =>
            bw.Write((byte[])[(byte)(v >> 56), (byte)(v >> 48), (byte)(v >> 40), (byte)(v >> 32), (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v]);
    }
}
