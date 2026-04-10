using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace XboxKit
{
    internal class XboxPRNG
    {
        private static readonly uint[] FIXED_SEEDS = { 0x52F690D5, 0x534D7DDE, 0x5B71A70F, 0x66793320, 0x9B7E5ED5, 0xA465265E, 0xA53F1D11, 0xB154430F };

        private uint State;
        private readonly uint Mult;
        private readonly uint Mask;

        // Constructor, generates mult/state/mask from initial seed
        public XboxPRNG(uint seed)
        {
            Mult = FIXED_SEEDS[seed & 7];
            State = (uint)(((seed + 1UL) * Mult) % 0xFFFFFFFB);
            Mask = State;
        }

        // Advance state variable as if sectors were being written
        public void SimulateSectors(long count)
        {
            for (int i = 0; i < count; i++)
                for (int j = 0; j < XDVDFS.SECTOR_SIZE; j += 2)
                    State = (uint)(((State + 1UL) * Mult) % 0xFFFFFFFB);
        }

        // Write a number of PRNG sectors to a filestream
        public void WriteSectors(FileStream fs, long count)
        {
            for (int i = 0; i < count; i++)
            {
                byte[] sector = GenerateSector();
                fs.Write(sector, 0, (int)XDVDFS.SECTOR_SIZE);
            }
        }

        // Generate a sector using the current state variables
        private byte[] GenerateSector()
        {
            byte[] sector = new byte[XDVDFS.SECTOR_SIZE];
            for (int j = 0; j < XDVDFS.SECTOR_SIZE; j += 2)
            {
                State = (uint)(((State + 1UL) * Mult) % 0xFFFFFFFB);
                ushort sample = (ushort)((State ^ Mask) >> 8);
                sector[j] = (byte)sample;
                sector[j+1] = (byte)(sample >> 8);
            }
            return sector;
        }

        // Brute force seed for pseudo random number generator
        public static bool TryGetSeed(byte[] sector, out uint outSeed)
        {
            uint foundSeed = 0;
            bool seedFound = false;

            const long MaxUInt32 = (long)uint.MaxValue + 1;
            var range = Partitioner.Create(0L, MaxUInt32);
            Parallel.ForEach(range, (chunk, state) =>
            {
                for (long i = chunk.Item1; i < chunk.Item2; i++)
                {
                    if (Volatile.Read(ref seedFound))
                        break;
                    uint seedGuess = (uint)i;
                    uint multGuess = XboxPRNG.FIXED_SEEDS[seedGuess & 7];
                    uint stateGuess = (uint)(((seedGuess + 1UL) * multGuess) % 0xFFFFFFFB);
                    uint maskAttempt = stateGuess;
                    bool match = true;
                    for (int j = 0; j < XDVDFS.SECTOR_SIZE * 2; j += 2)
                    {
                        stateGuess = (uint)(((stateGuess + 1UL) * multGuess) % 0xFFFFFFFB);
                        ushort sample = (ushort)((stateGuess ^ maskAttempt) >> 8);
                        if (sector[j] != (byte)sample || sector[j + 1] != (byte)(sample >> 8))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        Volatile.Write(ref foundSeed, seedGuess);
                        Volatile.Write(ref seedFound, true);
                        state.Stop();
                        break;
                    }
                }
            });

            outSeed = foundSeed;
            return seedFound;
        }
    }
}
