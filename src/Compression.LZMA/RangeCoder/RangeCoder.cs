#region Notice
// This code is taken from version 9.20 of the LZMA SDK.
// Some modifications have been made byte Jens Granlund

// LZMA SDK is written and placed in the public domain by Igor Pavlov.

// Some code in LZMA SDK is based on public domain code from another developers:
//   1) PPMd var.H (2001): Dmitry Shkarin
//   2) SHA-256: Wei Dai (Crypto++ library)
#endregion

using System;
using System.IO;

namespace SevenZip.RangeCoder
{
    internal class Encoder
    {
        public const uint KTopValue = (1 << 24);

        public UInt64 Low;
        public uint Range;

        private long _startPosition;
        private Stream _stream;
        private byte _cache;
        private uint _cacheSize;

        public void SetStream(Stream stream) { _stream = stream; }

        public void ReleaseStream() { _stream = null; }

        public void Init()
        {
            _startPosition = _stream.Position;

            Low = 0;
            Range = 0xFFFFFFFF;
            _cacheSize = 1;
            _cache = 0;
        }

        public void FlushData() { for (int i = 0; i < 5; i++) ShiftLow(); }

        public void FlushStream() { _stream.Flush(); }

        public void CloseStream() { _stream.Close(); }

        public void Encode(uint start, uint size, uint total)
        {
            Low += start*(Range /= total);
            Range *= size;
            while (Range < KTopValue)
            {
                Range <<= 8;
                ShiftLow();
            }
        }

        public void ShiftLow()
        {
            if ((uint) Low < 0xFF000000 || (uint) (Low >> 32) == 1)
            {
                byte temp = _cache;
                do
                {
                    _stream.WriteByte((byte) (temp + (Low >> 32)));
                    temp = 0xFF;
                } while (--_cacheSize != 0);
                _cache = (byte) (((uint) Low) >> 24);
            }
            _cacheSize++;
            Low = ((uint) Low) << 8;
        }

        public void EncodeDirectBits(uint v, int numTotalBits)
        {
            for (int i = numTotalBits - 1; i >= 0; i--)
            {
                Range >>= 1;
                if (((v >> i) & 1) == 1) Low += Range;
                if (Range < KTopValue)
                {
                    Range <<= 8;
                    ShiftLow();
                }
            }
        }

        public void EncodeBit(uint size0, int numTotalBits, uint symbol)
        {
            uint newBound = (Range >> numTotalBits)*size0;
            if (symbol == 0) Range = newBound;
            else
            {
                Low += newBound;
                Range -= newBound;
            }
            while (Range < KTopValue)
            {
                Range <<= 8;
                ShiftLow();
            }
        }

        public long GetProcessedSizeAdd()
        {
            return _cacheSize + _stream.Position - _startPosition + 4;
            // (long)Stream.GetProcessedSize();
        }
    }

    internal class Decoder
    {
        public const uint KTopValue = (1 << 24);
        public uint Code;
        public uint Range;
        // public Buffer.InBuffer Stream = new Buffer.InBuffer(1 << 16);
        public Stream Stream;

        public void Init(Stream stream)
        {
            // Stream.Init(stream);
            Stream = stream;

            Code = 0;
            Range = 0xFFFFFFFF;
            for (int i = 0; i < 5; i++) Code = (Code << 8) | (byte) Stream.ReadByte();
        }

        public void ReleaseStream()
        {
            // Stream.ReleaseStream();
            Stream = null;
        }

        public void CloseStream() { Stream.Close(); }

        public void Normalize()
        {
            while (Range < KTopValue)
            {
                Code = (Code << 8) | (byte) Stream.ReadByte();
                Range <<= 8;
            }
        }

        public void Normalize2()
        {
            if (Range < KTopValue)
            {
                Code = (Code << 8) | (byte) Stream.ReadByte();
                Range <<= 8;
            }
        }

        public uint GetThreshold(uint total) { return Code/(Range /= total); }

        public void Decode(uint start, uint size, uint total)
        {
            Code -= start*Range;
            Range *= size;
            Normalize();
        }

        public uint DecodeDirectBits(int numTotalBits)
        {
            uint range = Range;
            uint code = Code;
            uint result = 0;
            for (int i = numTotalBits; i > 0; i--)
            {
                range >>= 1;
                /*
				result <<= 1;
				if (code >= range)
				{
					code -= range;
					result |= 1;
				}
				*/
                uint t = (code - range) >> 31;
                code -= range & (t - 1);
                result = (result << 1) | (1 - t);

                if (range < KTopValue)
                {
                    code = (code << 8) | (byte) Stream.ReadByte();
                    range <<= 8;
                }
            }
            Range = range;
            Code = code;
            return result;
        }

        public uint DecodeBit(uint size0, int numTotalBits)
        {
            uint newBound = (Range >> numTotalBits)*size0;
            uint symbol;
            if (Code < newBound)
            {
                symbol = 0;
                Range = newBound;
            }
            else
            {
                symbol = 1;
                Code -= newBound;
                Range -= newBound;
            }
            Normalize();
            return symbol;
        }

        // ulong GetProcessedSize() {return Stream.GetProcessedSize(); }
    }
}