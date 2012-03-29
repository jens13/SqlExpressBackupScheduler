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

namespace SevenZip
{
    internal interface IInWindowStream
    {
        void SetStream(Stream inStream);
        void Init();
        void ReleaseStream();
        Byte GetIndexByte(Int32 index);
        UInt32 GetMatchLen(Int32 index, UInt32 distance, UInt32 limit);
        UInt32 GetNumAvailableBytes();
    }

    internal interface IMatchFinder : IInWindowStream
    {
        void Create(UInt32 historySize, UInt32 keepAddBufferBefore, UInt32 matchMaxLen, UInt32 keepAddBufferAfter);
        UInt32 GetMatches(UInt32[] distances);
        void Skip(UInt32 num);
    }
}