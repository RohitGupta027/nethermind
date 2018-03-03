﻿using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Encoding;
using Nethermind.Core.Extensions;

namespace Nethermind.Mining
{
    public class Ethash
    {
        public const int WordBytes = 4; // bytes in word
        public ulong DatasetBytesInit = (ulong)BigInteger.Pow(2, 30); // bytes in dataset at genesis
        public ulong DatasetBytesGrowth = (ulong)BigInteger.Pow(2, 23); // dataset growth per epoch
        public ulong CacheBytesInit = (ulong)BigInteger.Pow(2, 24); // bytes in cache at genesis
        public ulong CacheBytesGrowth = (ulong)BigInteger.Pow(2, 17); // cache growth per epoch
        public const int CacheMultiplier = 1024; // Size of the DAG relative to the cache
        public const ulong EpochLength = 30000; // blocks per epoch
        public const uint MixBytes = 128; // width of mix
        public const uint HashBytes = 64; // hash length in bytes
        public const int DatasetParents = 256; // number of parents of each dataset element
        public const int CacheRounds = 3; // number of rounds in cache production
        public const int Accesses = 64; // number of accesses in hashimoto loop

        public ulong GetDataSize(BigInteger blockNumber)
        {
            ulong size = DatasetBytesInit + DatasetBytesGrowth * ((ulong)blockNumber / EpochLength);
            size -= MixBytes;
            while (!IsPrime(size / MixBytes))
            {
                size -= 2 * MixBytes;
            }

            return size;
        }

        public ulong GetCacheSize(BigInteger blockNumber)
        {
            ulong size = CacheBytesInit + CacheBytesGrowth * ((ulong)blockNumber / EpochLength);
            size -= HashBytes;
            while (!IsPrime(size / HashBytes))
            {
                size -= 2 * HashBytes;
            }

            return size;
        }

        public static bool IsPrime(ulong number)
        {
            if (number == 1)
            {
                return false;
            }

            if (number == 2 || number == 3)
            {
                return true;
            }

            if (number % 2 == 0 || number % 3 == 0)
            {
                return false;
            }

            ulong w = 2;
            ulong i = 5;
            while (i * i <= number)
            {
                if (number % i == 0)
                {
                    return false;
                }

                i += w;
                w = 6 - w;
            }

            return true;
        }

        public Keccak GetSeedHash(BigInteger blockNumber)
        {
            byte[] seed = new byte[32];
            for (int i = 0; i < blockNumber / EpochLength; i++)
            {
                seed = Keccak.Compute(seed).Bytes; // TODO: optimize
            }

            return new Keccak(seed);
        }

        private readonly BigInteger _2To256 = BigInteger.Pow(2, 256);

        private static readonly Random Random = new Random();

        private static ulong GetRandomNonce()
        {
            byte[] buffer = new byte[8];
            Random.NextBytes(buffer);
            return BitConverter.ToUInt64(buffer, 0);
        }

        private bool IsGreaterThanTarget(byte[] result, byte[] target)
        {
            throw new NotImplementedException();
        }

        private ulong Mine(ulong fullSize, byte[][] dataSet, BlockHeader header, BigInteger difficulty, Func<ulong, byte[][], BlockHeader, ulong, (byte[], byte[])> hashimoto)
        {
            ulong nonce = GetRandomNonce();
            byte[] target = BigInteger.Divide(_2To256, difficulty).ToBigEndianByteArray();
            (byte[] _, byte[] result) = hashimoto(fullSize, dataSet, header, nonce);
            while (IsGreaterThanTarget(result, target))
            {
                unchecked
                {
                    nonce += 1;
                }
            }

            return nonce;
        }

        public ulong MineFull(ulong fullSize, byte[][] dataSet, BlockHeader header, BigInteger difficulty)
        {
            return Mine(fullSize, dataSet, header, difficulty, HashimotoFull);
        }

        public ulong MineLight(ulong fullSize, byte[][] cache, BlockHeader header, BigInteger difficulty)
        {
            return Mine(fullSize, cache, header, difficulty, HashimotoLight);
        }

        public byte[][] BuildDataSet(ulong setSize, byte[][] cache)
        {
            Console.WriteLine($"building data set of length {setSize}"); // TODO: temp, remove
            byte[][] dataSet = new byte[(uint)(setSize / HashBytes)][];
            for (uint i = 0; i < dataSet.Length; i++)
            {
                if (i % 100000 == 0)
                {
                    Console.WriteLine($"building data set of length {setSize}, built {i}"); // TODO: temp, remove
                }

                dataSet[i] = CalcDataSetItem(i, cache);
            }

            return dataSet;
        }

        // TODO: optimize, check, work in progress
        // this data set will be in GBs
        private byte[] CalcDataSetItem(uint index, byte[][] cache)
        {
            ulong r = HashBytes / WordBytes;
            ulong cacheSize = (ulong)cache.Length;

            byte[] mix = (byte[])cache[index % cacheSize].Clone();
            SetUInt(mix, 0, index ^ GetUInt(mix, 0));
            mix = Keccak512.Compute(mix).Bytes;

            for (ulong i = 0; i < DatasetParents; i++)
            {
                ulong cacheIndex = Fnv(index ^ i, mix[i % r]);
                Fnv(mix, cache[cacheIndex % cacheSize]); // TODO: check
//                mix = Fnv(mix, cache[cacheIndex % cacheSize]); // TODO: check
            }

            return Keccak512.Compute(mix).Bytes;
        }

        // TODO: optimize, check, work in progress
        public byte[][] MakeCache(ulong cacheSize, byte[] seed)
        {
            byte[][] cache = new byte[cacheSize / HashBytes][];
            cache[0] = Keccak512.Compute(seed).Bytes;
            for (uint i = 1; i < cache.Length; i++)
            {
                cache[i] = Keccak512.Compute(cache[i - 1]).Bytes;
            }

            for (int i = 0; i < CacheRounds; i++)
            {
                for (int j = 0; j < cache.Length; j++)
                {
                    int v = cache[j][0] % cache.Length;
                    cache[j] = Keccak512.Compute(cache[(i - 1 + cache.Length) % cache.Length].Xor(cache[v])).Bytes;
                }
            }

            return cache;
        }

        public const uint FnvPrime = 0x01000193;

        public const ulong FnvPrimeLong = 0x01000193;

        // TODO: optimize, check, work in progress
        private static byte[] Fnv(byte[] b1, byte[] b2)
        {
//            Debug.Assert(b1.Length == b2.Length, "FNV expecting same length arrays");
//            Debug.Assert(b1.Length % 4 == 0, "FNV expecting length to be a multiple of 4");

            // TODO: check this thing (in place calc)
//            byte[] result = new byte[b1.Length];
            for (uint i = 0; i < b1.Length / 4; i++)
            {
                uint v1 = GetUInt(b1, i);
                uint v2 = GetUInt(b2, i);
                SetUInt(b1, i, Fnv(v1, v2));
            }

            return b1;
        }

        private static void SetUInt(byte[] bytes, uint offset, uint value)
        {
            byte[] valueBytes = value.ToByteArray(Bytes.Endianness.Little);
            Buffer.BlockCopy(valueBytes, 0, bytes, (int)offset, 4);
        }

        private static uint GetUInt(byte[] bytes, uint offset)
        {
            return bytes.Slice((int)offset, 4).ToUInt32(Bytes.Endianness.Little);
        }

        private static ulong Fnv(ulong v1, ulong v2)
        {
            return (v1 * FnvPrimeLong) ^ v2;
        }

        private static uint Fnv(uint v1, uint v2)
        {
            return (v1 * FnvPrime) ^ v2;
        }

        public bool Validate(BlockHeader header)
        {
            ulong fullSize = GetDataSize(header.Number);
            ulong cacheSize = GetCacheSize(header.Number);
            Keccak seed = GetSeedHash(header.Number);
            byte[][] cache = MakeCache(cacheSize, seed.Bytes); // TODO: load cache

            (byte[] _, byte[] result) = HashimotoLight(fullSize, cache, header, header.Nonce);

            BigInteger threshold = BigInteger.Divide(BigInteger.Pow(2, 256), header.Difficulty);
            BigInteger resultAsInteger = result.ToUnsignedBigInteger();
            return resultAsInteger < threshold;
        }

        private (byte[], byte[]) Hashimoto(ulong fullSize, BlockHeader header, ulong nonce, Func<uint, byte[]> getDataSetItem)
        {
            uint hashesInFull = (uint)(fullSize / HashBytes);
            uint wordsInHash = MixBytes / WordBytes;
            uint hashesInMix = MixBytes / HashBytes;
            byte[] headerHashed = Keccak.Compute(Rlp.Encode(header, false)).Bytes; // sic! Keccak here not Keccak512
            byte[] headerAndNonceHashed = Keccak512.Compute(Bytes.Concat(headerHashed, nonce.ToBigEndianByteArray())).Bytes;
            byte[] mix = new byte[MixBytes];
            for (int i = 0; i < hashesInMix; i++)
            {
                Buffer.BlockCopy(headerAndNonceHashed, 0, mix, i * headerAndNonceHashed.Length, headerAndNonceHashed.Length);
            }

            for (uint i = 0; i < Accesses; i++)
            {
                uint p = Fnv(i ^ GetUInt(headerAndNonceHashed, 0), GetUInt(mix, i % wordsInHash)) % (hashesInFull / hashesInMix) * hashesInMix; // TODO: check as ethereumJ uses hashesInFullSize here but it seems that they got it wrong...
                byte[] newData = new byte[MixBytes];
                for (uint j = 0; j < hashesInMix; j++)
                {
                    byte[] item1 = getDataSetItem(p * hashesInMix + j);
                    Buffer.BlockCopy(item1, 0, newData, (int)(j * item1.Length), item1.Length);
                }

//                mix = Fnv(mix, newData);
                Fnv(mix, newData);
            }


            byte[] cmix = new byte[mix.Length / 4];
            for (uint i = 0; i < mix.Length / 4; i += 4 /* ? */)
            {
                uint fnv1 = Fnv(GetUInt(mix, i), GetUInt(mix, i + 1));
                uint fnv2 = Fnv(fnv1, GetUInt(mix, i + 2));
                uint fnv3 = Fnv(fnv2, GetUInt(mix, i + 3));
                SetUInt(cmix, i / 4, fnv3);
            }

            return (cmix, Keccak.Compute(Bytes.Concat(headerAndNonceHashed, cmix)).Bytes);
        }

        public (byte[], byte[]) HashimotoLight(ulong fullSize, byte[][] cache, BlockHeader header, ulong nonce)
        {
            return Hashimoto(fullSize, header, nonce, index => CalcDataSetItem(index, cache));
        }

        public (byte[], byte[]) HashimotoFull(ulong fullSize, byte[][] dataSet, BlockHeader header, ulong nonce)
        {
            return Hashimoto(fullSize, header, nonce, index => dataSet[index]);
        }
    }
}