#region Notice
// This code is taken from version 9.20 of the LZMA SDK.
// Some modifications have been made byte Jens Granlund

// LZMA SDK is written and placed in the public domain by Igor Pavlov.

// Some code in LZMA SDK is based on public domain code from another developers:
//   1) PPMd var.H (2001): Dmitry Shkarin
//   2) SHA-256: Wei Dai (Crypto++ library)
#endregion
namespace SevenZip.LZMA
{
    internal abstract class Base
    {
        public const uint KNumRepDistances = 4;
        public const uint KNumStates = 12;

        // static byte []kLiteralNextStates  = {0, 0, 0, 0, 1, 2, 3, 4,  5,  6,   4, 5};
        // static byte []kMatchNextStates    = {7, 7, 7, 7, 7, 7, 7, 10, 10, 10, 10, 10};
        // static byte []kRepNextStates      = {8, 8, 8, 8, 8, 8, 8, 11, 11, 11, 11, 11};
        // static byte []kShortRepNextStates = {9, 9, 9, 9, 9, 9, 9, 11, 11, 11, 11, 11};

        public const int KNumPosSlotBits = 6;
        public const int KDicLogSizeMin = 0;
        // public const int kDicLogSizeMax = 30;
        // public const uint kDistTableSizeMax = kDicLogSizeMax * 2;

        public const int KNumLenToPosStatesBits = 2; // it's for speed optimization
        public const uint KNumLenToPosStates = 1 << KNumLenToPosStatesBits;

        public const uint KMatchMinLen = 2;

        public const int KNumAlignBits = 4;
        public const uint KAlignTableSize = 1 << KNumAlignBits;
        public const uint KAlignMask = (KAlignTableSize - 1);

        public const uint KStartPosModelIndex = 4;
        public const uint KEndPosModelIndex = 14;
        public const uint KNumPosModels = KEndPosModelIndex - KStartPosModelIndex;

        public const uint KNumFullDistances = 1 << ((int) KEndPosModelIndex/2);

        public const uint KNumLitPosStatesBitsEncodingMax = 4;
        public const uint KNumLitContextBitsMax = 8;

        public const int KNumPosStatesBitsMax = 4;
        public const uint KNumPosStatesMax = (1 << KNumPosStatesBitsMax);
        public const int KNumPosStatesBitsEncodingMax = 4;
        public const uint KNumPosStatesEncodingMax = (1 << KNumPosStatesBitsEncodingMax);

        public const int KNumLowLenBits = 3;
        public const int KNumMidLenBits = 3;
        public const int KNumHighLenBits = 8;
        public const uint KNumLowLenSymbols = 1 << KNumLowLenBits;
        public const uint KNumMidLenSymbols = 1 << KNumMidLenBits;
        public const uint KNumLenSymbols = KNumLowLenSymbols + KNumMidLenSymbols + (1 << KNumHighLenBits);
        public const uint KMatchMaxLen = KMatchMinLen + KNumLenSymbols - 1;

        public static uint GetLenToPosState(uint len)
        {
            len -= KMatchMinLen;
            if (len < KNumLenToPosStates) return len;
            return (KNumLenToPosStates - 1);
        }

        #region Nested type: State

        public struct State
        {
            public uint Index;
            public void Init() { Index = 0; }

            public void UpdateChar()
            {
                if (Index < 4) Index = 0;
                else if (Index < 10) Index -= 3;
                else Index -= 6;
            }

            public void UpdateMatch() { Index = (uint) (Index < 7 ? 7 : 10); }
            public void UpdateRep() { Index = (uint) (Index < 7 ? 8 : 11); }
            public void UpdateShortRep() { Index = (uint) (Index < 7 ? 9 : 11); }
            public bool IsCharState() { return Index < 7; }
        }

        #endregion
    }
}