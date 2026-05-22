using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SharpCompress.Compressors.ZStandard;

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
            public bool IsFile;
            public int NameIndex;
            public List<PathNode> Subnodes = new();
            public long SourceOffset;
            public ulong FileOffset;
            public ulong FileSize;
            public uint NodeStartIndex;
        }

        // Stream wrapper that hashes all written bytes
        private class HashingStream(FileStream fs)
        {
            private readonly FileStream _fs = fs;
            private readonly SHA256 _sha = SHA256.Create();
            public long Position;

            public void Write(byte[] buf, int offset, int count)
            {
                _fs.Write(buf, offset, count);
                _sha.TransformBlock(buf, offset, count, null, 0);
                Position += count;
            }

            public void Write(byte b)
            {
                _fs.WriteByte(b);
                _sha.TransformBlock([b], 0, 1, null, 0);
                Position++;
            }

            public void Write(ulong v) =>
                Write([(byte)(v >> 56), (byte)(v >> 48), (byte)(v >> 40), (byte)(v >> 32),
                       (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v], 0, 8);

            public void Write(uint v) =>
                Write([(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v], 0, 4);

            public void Write(ushort v) =>
                Write([(byte)(v >> 8), (byte)v], 0, 2);

            public byte[] FinalizeHash(byte[] lastBlock)
            {
                _sha.TransformFinalBlock(lastBlock, 0, lastBlock.Length);
                return _sha.Hash!;
            }
        }

        // Create ZArchive from game files in an XISO
        public static bool CreateZAR(FileStream isoFS, long xisoOffset, string zarPath, bool quiet)
        {
            // Parse XDVDFS volume descriptor to get root directory
            long headerOffset = xisoOffset + XDVDFS.XISO_HEADER_OFFSET;
            isoFS.Seek(headerOffset + 20, SeekOrigin.Begin);
            uint rootSector = Utils.ReadUInt(isoFS);
            uint rootSize = Utils.ReadUInt(isoFS);

            // Build path tree from XDVDFS
            var names = new List<string>();
            var nameLookup = new Dictionary<string, int>();
            var rootNode = new PathNode { IsFile = false, NameIndex = GetOrAddName(names, nameLookup, "") };
            BuildPathTree(isoFS, xisoOffset, (long)rootSector * XDVDFS.SECTOR_SIZE, rootSize, 0, rootNode, names, nameLookup);

            // Collect files in BFS order (determines data layout)
            var allFiles = new List<PathNode>();
            CollectFiles(rootNode, allFiles, names);

            if (!quiet) Console.WriteLine($"[INFO] Writing ZArchive to {zarPath}");

            // Write ZArchive
            using FileStream zarFS = new(zarPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            var hs = new HashingStream(zarFS);

            if (!WriteCompressedData(isoFS, xisoOffset, hs, allFiles, out var offsetRecords))
                return false;
            ulong compressedDataSize = (ulong)hs.Position;

            while (hs.Position % 8 != 0) hs.Write((byte)0);

            ulong offsetRecordsStart = (ulong)hs.Position;
            WriteOffsetRecords(hs, offsetRecords);

            ulong nameTableStart = (ulong)hs.Position;
            var nameOffsets = new uint[names.Count];
            WriteNameTable(hs, names, nameOffsets);

            ulong fileTreeStart = (ulong)hs.Position;
            WriteFileTree(hs, rootNode, names, nameOffsets);

            WriteFooter(zarFS, hs, compressedDataSize, offsetRecordsStart, nameTableStart, fileTreeStart);
            return true;
        }

        // Recursively build path tree from XDVDFS directory structure
        private static void BuildPathTree(FileStream isoFS, long isoOffset, long dirOffset, uint dirSize, long childOffset,
            PathNode parentNode, List<string> names, Dictionary<string, int> nameLookup)
        {
            if (childOffset >= dirSize)
                return;

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

            if (leftChild != 0 && leftChild != 0xFFFF)
                BuildPathTree(isoFS, isoOffset, dirOffset, dirSize, (long)leftChild * 4, parentNode, names, nameLookup);

            int nameIndex = GetOrAddName(names, nameLookup, name);
            var node = new PathNode { IsFile = !isDirectory, NameIndex = nameIndex };

            if (isDirectory)
                BuildPathTree(isoFS, isoOffset, entryOffset, entrySize, 0, node, names, nameLookup);
            else
            {
                node.SourceOffset = entryOffset;
                node.FileSize = entrySize;
            }

            parentNode.Subnodes.Add(node);

            if (rightChild != 0 && rightChild != 0xFFFF)
                BuildPathTree(isoFS, isoOffset, dirOffset, dirSize, (long)rightChild * 4, parentNode, names, nameLookup);
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

        // Collect files in BFS order (same order as file tree serialization)
        private static void CollectFiles(PathNode root, List<PathNode> files, List<string> names)
        {
            var queue = new Queue<PathNode>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (node.IsFile) { files.Add(node); continue; }
                node.Subnodes.Sort((a, b) => CompareNodeName(names[a.NameIndex], names[b.NameIndex]));
                foreach (var child in node.Subnodes)
                    queue.Enqueue(child);
            }
        }

        // Case-insensitive name comparison matching ZArchive canonical ordering
        private static int CompareNodeName(string n1, string n2)
        {
            int minLen = Math.Min(n1.Length, n2.Length);
            for (int i = 0; i < minLen; i++)
            {
                char c1 = n1[i];
                char c2 = n2[i];
                if (c1 >= 'A' && c1 <= 'Z') c1 = (char)(c1 + ('a' - 'A'));
                if (c2 >= 'A' && c2 <= 'Z') c2 = (char)(c2 + ('a' - 'A'));
                if (c1 != c2)
                    return (int)(byte)c1 - (int)(byte)c2;
            }
            if (n1.Length < n2.Length) return -1;
            if (n1.Length > n2.Length) return 1;
            return 0;
        }

        // Write all file data as Zstd-compressed 64KB blocks
        private static bool WriteCompressedData(FileStream isoFS, long xisoOffset, HashingStream hs,
            List<PathNode> allFiles, out List<(ulong BaseOffset, ushort[] Sizes)> offsetRecords)
        {
            offsetRecords = new List<(ulong BaseOffset, ushort[] Sizes)>();
            ushort[] sizes = new ushort[BLOCKS_PER_RECORD];
            int count = 0;
            ulong recordBase = 0;

            byte[] buf = new byte[BLOCK_SIZE];
            int bufPos = 0;
            ulong inputOffset = 0;

            foreach (var file in allFiles)
            {
                file.FileOffset = inputOffset;
                isoFS.Seek(xisoOffset + file.SourceOffset, SeekOrigin.Begin);
                long remaining = (long)file.FileSize;

                while (remaining > 0)
                {
                    int toRead = (int)Math.Min(BLOCK_SIZE - bufPos, remaining);
                    int bytesRead = isoFS.Read(buf, bufPos, toRead);
                    if (bytesRead == 0) return false;

                    bufPos += bytesRead;
                    remaining -= bytesRead;
                    inputOffset += (ulong)bytesRead;

                    if (bufPos == BLOCK_SIZE)
                    {
                        FlushBlock(hs, buf, offsetRecords, ref sizes, ref count, ref recordBase);
                        bufPos = 0;
                    }
                }
            }

            if (bufPos > 0)
            {
                Array.Clear(buf, bufPos, BLOCK_SIZE - bufPos);
                FlushBlock(hs, buf, offsetRecords, ref sizes, ref count, ref recordBase);
            }

            if (count > 0)
                offsetRecords.Add((recordBase, sizes));

            return true;
        }

        // Compress and write a single 64KB block, tracking offset records
        private static void FlushBlock(HashingStream hs, byte[] data,
            List<(ulong BaseOffset, ushort[] Sizes)> offsetRecords,
            ref ushort[] sizes, ref int count, ref ulong recordBase)
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
            byte[] compressed;
            var ms = new MemoryStream();
            using (var zstd = new ZStandardStream(ms, 6, false))
                zstd.Write(data, 0, BLOCK_SIZE);
            compressed = ms.ToArray();

            bool useRaw = compressed.Length >= BLOCK_SIZE;
            byte[] toWrite = useRaw ? data : compressed;
            int storedSize = useRaw ? BLOCK_SIZE : compressed.Length;

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

        // Write name table section
        private static void WriteNameTable(HashingStream hs, List<string> names, uint[] nameOffsets)
        {
            uint pos = 0;
            for (int i = 0; i < names.Count; i++)
            {
                nameOffsets[i] = pos;
                byte[] nameBytes = Encoding.UTF8.GetBytes(names[i]);
                int len = nameBytes.Length;
                if (len >= 0x80)
                {
                    byte[] header = [(byte)((len & 0x7F) | 0x80), (byte)(len >> 7)];
                    hs.Write(header, 0, 2);
                    pos += 2;
                }
                else
                {
                    hs.Write([(byte)(len & 0x7F)], 0, 1);
                    pos += 1;
                }
                hs.Write(nameBytes, 0, nameBytes.Length);
                pos += (uint)nameBytes.Length;
            }
        }

        // Write file tree section (BFS order)
        private static void WriteFileTree(HashingStream hs, PathNode rootNode, List<string> names, uint[] nameOffsets)
        {
            // Flatten tree in BFS order and assign indices
            var nodes = new List<PathNode>();
            var queue = new Queue<PathNode>();
            queue.Enqueue(rootNode);
            uint idx = 1;
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                nodes.Add(node);
                if (node.IsFile) continue;
                node.Subnodes.Sort((a, b) => CompareNodeName(names[a.NameIndex], names[b.NameIndex]));
                node.NodeStartIndex = idx;
                idx += (uint)node.Subnodes.Count;
                foreach (var child in node.Subnodes)
                    queue.Enqueue(child);
            }

            // Serialize all entries
            foreach (var node in nodes)
            {
                uint flag = node == rootNode ? 0x7FFFFFFF
                    : node.IsFile ? 0x80000000 | nameOffsets[node.NameIndex]
                    : nameOffsets[node.NameIndex];
                hs.Write(flag);

                if (node.IsFile)
                {
                    hs.Write((uint)(node.FileOffset & 0xFFFFFFFF));
                    hs.Write((uint)(node.FileSize & 0xFFFFFFFF));
                    hs.Write((ushort)(node.FileSize >> 32));
                    hs.Write((ushort)(node.FileOffset >> 32));
                }
                else
                {
                    hs.Write(node.NodeStartIndex);
                    hs.Write((uint)node.Subnodes.Count);
                    hs.Write((uint)0);
                }
            }
        }

        // Write footer with SHA-256 integrity hash
        private static void WriteFooter(FileStream zarFS, HashingStream hs,
            ulong compressedDataSize, ulong offsetRecordsStart, ulong nameTableStart, ulong fileTreeStart)
        {
            ulong end = (ulong)hs.Position;
            ulong totalSize = end + 144;

            using var ms = new MemoryStream(144);
            using var bw = new BinaryWriter(ms);
            WriteBE(bw, (ulong)0);                          // compressed data offset
            WriteBE(bw, compressedDataSize);
            WriteBE(bw, offsetRecordsStart);
            WriteBE(bw, nameTableStart - offsetRecordsStart);
            WriteBE(bw, nameTableStart);
            WriteBE(bw, fileTreeStart - nameTableStart);
            WriteBE(bw, fileTreeStart);
            WriteBE(bw, end - fileTreeStart);
            WriteBE(bw, end);                               // meta directory offset
            WriteBE(bw, (ulong)0);
            WriteBE(bw, end);                               // metadata offset
            WriteBE(bw, (ulong)0);
            bw.Write(new byte[32]);                         // integrity hash (zeroed for hashing)
            WriteBE(bw, totalSize);
            bw.Write(VERSION1);
            bw.Write(MAGIC);

            byte[] footerBytes = ms.ToArray();
            byte[] hash = hs.FinalizeHash(footerBytes);
            Array.Copy(hash, 0, footerBytes, 96, 32);
            zarFS.Write(footerBytes, 0, footerBytes.Length);
        }

        // Big-endian write helper for BinaryWriter (footer construction only)
        private static void WriteBE(BinaryWriter bw, ulong v) =>
            bw.Write((byte[])[(byte)(v >> 56), (byte)(v >> 48), (byte)(v >> 40), (byte)(v >> 32),
                              (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v]);
    }
}
