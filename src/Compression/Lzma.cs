#region Copyright (c) 2012, Jens Granlund
// Copyright (c) 2012, Jens Granlund
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without 
// modification, are permitted provided that the following conditions are met:
// 
// - Redistributions of source code must retain the above copyright notice, this 
//   list of conditions and the following disclaimer.
// - Redistributions in binary form must reproduce the above copyright notice, 
//   this list of conditions and the following disclaimer in the documentation 
//   and/or other materials provided with the distribution.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND 
// ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED 
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE 
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE 
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL 
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR 
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER 
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE 
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
// 
// More info: http://www.opensource.org/licenses/bsd-license.php
#endregion

using System;
using System.IO;
using SevenZip.LZMA;

namespace SebScheduler.Compression
{
    public static class Lzma 
    {
        public static void Compress(string uncompressedFile, string compressedFile, LzmaProperties properties)
        {
            using (var uncompressed = File.Open(uncompressedFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var compressed = File.Open(compressedFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            {
                Compress(uncompressed, compressed, properties);
            }
        }
        public static void Compress(Stream uncompressed, Stream compressed, LzmaProperties properties)
        {
            var encoder = new Encoder();
            encoder.SetCoderProperties(properties.Identifiers, properties.Values);
            encoder.WriteCoderProperties(compressed);
            WriteSize(uncompressed.Length, compressed);
            encoder.Code(uncompressed, compressed, -1, -1, null);
        }
        private static void WriteSize(long size, Stream stream)
        {
            for (int i = 0; i < 8; i++)
            {
                stream.WriteByte((Byte)(size >> (8 * i)));
            }
        }
        public static void Decompress(string compressedFile, string uncompressedFile)
        {
            using (var compressed = File.Open(compressedFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var uncompressed = File.Open(uncompressedFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            {
                Decompress(uncompressed, compressed);
            }
        }
        public static void Decompress(Stream compressed, Stream uncompressed)
        {
            if (compressed.Length < 14) throw new Exception("Compressed stream is to small to be containing any data");
            var decoder = new Decoder();
			var properties = new byte[5];
            compressed.Read(properties, 0, 5);
            decoder.SetDecoderProperties(properties);
            long uncompressedSize = ReadSize(compressed);
            long compressedSize = compressed.Length - compressed.Position;
            decoder.Code(compressed, uncompressed, compressedSize, uncompressedSize, null);
        }
        private static long ReadSize(Stream stream)
        {
            long size = 0;
            for (int i = 0; i < 8; i++)
            {
                size |= ((long)stream.ReadByte()) << (8 * i);
            }
            return size;
        }
    }
}