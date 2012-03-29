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
using SevenZip.Compression.RangeCoder;
using SevenZip.LZ;
using SevenZip.RangeCoder;

namespace SevenZip.LZMA
{
    public class Encoder : ICoder, ISetCoderProperties, IWriteCoderProperties
    {
        private const UInt32 KIfinityPrice = 0xFFFFFFF;

        private const int KDefaultDictionaryLogSize = 22;
        private const UInt32 KNumFastBytesDefault = 0x20;

        private const UInt32 KNumLenSpecSymbols = Base.KNumLowLenSymbols + Base.KNumMidLenSymbols;

        private const UInt32 KNumOpts = 1 << 12;
        private const int KPropSize = 5;
        private static readonly Byte[] GFastPos = new Byte[1 << 11];
        private static readonly string[] KMatchFinderIDs = {"BT2", "BT4",};
        private readonly UInt32[] _alignPrices = new UInt32[Base.KAlignTableSize];
        private readonly UInt32[] _distancesPrices = new UInt32[Base.KNumFullDistances << Base.KNumLenToPosStatesBits];

        private readonly BitEncoder[] _isMatch = new BitEncoder[Base.KNumStates << Base.KNumPosStatesBitsMax];
        private readonly BitEncoder[] _isRep = new BitEncoder[Base.KNumStates];
        private readonly BitEncoder[] _isRep0Long = new BitEncoder[Base.KNumStates << Base.KNumPosStatesBitsMax];
        private readonly BitEncoder[] _isRepG0 = new BitEncoder[Base.KNumStates];
        private readonly BitEncoder[] _isRepG1 = new BitEncoder[Base.KNumStates];
        private readonly BitEncoder[] _isRepG2 = new BitEncoder[Base.KNumStates];

        private readonly LenPriceTableEncoder _lenEncoder = new LenPriceTableEncoder();

        private readonly LiteralEncoder _literalEncoder = new LiteralEncoder();

        private readonly UInt32[] _matchDistances = new UInt32[Base.KMatchMaxLen*2 + 2];
        private readonly Optimal[] _optimum = new Optimal[KNumOpts];
        private readonly BitEncoder[] _posEncoders = new BitEncoder[Base.KNumFullDistances - Base.KEndPosModelIndex];
        private readonly BitTreeEncoder[] _posSlotEncoder = new BitTreeEncoder[Base.KNumLenToPosStates];

        private readonly UInt32[] _posSlotPrices = new UInt32[1 << (Base.KNumPosSlotBits + Base.KNumLenToPosStatesBits)];
        private readonly RangeCoder.Encoder _rangeEncoder = new RangeCoder.Encoder();
        private readonly UInt32[] _repDistances = new UInt32[Base.KNumRepDistances];
        private readonly LenPriceTableEncoder _repMatchLenEncoder = new LenPriceTableEncoder();
        private readonly Byte[] _properties = new Byte[KPropSize];
        private readonly UInt32[] _repLens = new UInt32[Base.KNumRepDistances];
        private readonly UInt32[] _reps = new UInt32[Base.KNumRepDistances];
        private readonly UInt32[] _tempPrices = new UInt32[Base.KNumFullDistances];
        private UInt32 _additionalOffset;
        private UInt32 _alignPriceCount;

        private UInt32 _dictionarySize = (1 << KDefaultDictionaryLogSize);
        private UInt32 _dictionarySizePrev = 0xFFFFFFFF;
        private UInt32 _distTableSize = (KDefaultDictionaryLogSize*2);
        private bool _finished;
        private Stream _inStream;
        private UInt32 _longestMatchLength;
        private bool _longestMatchWasFound;
        private IMatchFinder _matchFinder;

        private EMatchFinderType _matchFinderType = EMatchFinderType.BT4;
        private UInt32 _matchPriceCount;

        private bool _needReleaseMFStream;
        private UInt32 _numDistancePairs;
        private UInt32 _numFastBytes = KNumFastBytesDefault;
        private UInt32 _numFastBytesPrev = 0xFFFFFFFF;
        private int _numLiteralContextBits = 3;
        private int _numLiteralPosStateBits;
        private UInt32 _optimumCurrentIndex;
        private UInt32 _optimumEndIndex;
        private BitTreeEncoder _posAlignEncoder = new BitTreeEncoder(Base.KNumAlignBits);
        private int _posStateBits = 2;
        private UInt32 _posStateMask = (4 - 1);
        private Byte _previousByte;
        private Base.State _state;
        private uint _trainSize;
        private bool _writeEndMark;
        private Int64 _nowPos64;

        static Encoder()
        {
            const Byte kFastSlots = 22;
            int c = 2;
            GFastPos[0] = 0;
            GFastPos[1] = 1;
            for (Byte slotFast = 2; slotFast < kFastSlots; slotFast++)
            {
                UInt32 k = ((UInt32) 1 << ((slotFast >> 1) - 1));
                for (UInt32 j = 0; j < k; j++, c++) GFastPos[c] = slotFast;
            }
        }

        public Encoder()
        {
            for (int i = 0; i < KNumOpts; i++) _optimum[i] = new Optimal();
            for (int i = 0; i < Base.KNumLenToPosStates; i++) _posSlotEncoder[i] = new BitTreeEncoder(Base.KNumPosSlotBits);
        }

        #region ICoder Members

        public void Code(Stream inStream, Stream outStream, Int64 inSize, Int64 outSize, ICodeProgress progress)
        {
            _needReleaseMFStream = false;
            try
            {
                SetStreams(inStream, outStream, inSize, outSize);
                while (true)
                {
                    Int64 processedInSize;
                    Int64 processedOutSize;
                    bool finished;
                    CodeOneBlock(out processedInSize, out processedOutSize, out finished);
                    if (finished) return;
                    if (progress != null)
                    {
                        progress.SetProgress(processedInSize, processedOutSize);
                    }
                }
            }
            finally
            {
                ReleaseStreams();
            }
        }

        #endregion

        #region ISetCoderProperties Members

        public void SetCoderProperties(CoderPropID[] propIDs, object[] properties)
        {
            for (UInt32 i = 0; i < properties.Length; i++)
            {
                object prop = properties[i];
                switch (propIDs[i])
                {
                    case CoderPropID.NumFastBytes:
                        {
                            if (!(prop is Int32)) throw new InvalidParamException();
                            var numFastBytes = (Int32) prop;
                            if (numFastBytes < 5 || numFastBytes > Base.KMatchMaxLen) throw new InvalidParamException();
                            _numFastBytes = (UInt32) numFastBytes;
                            break;
                        }
                    case CoderPropID.Algorithm:
                        {
                            /*
						if (!(prop is Int32))
							throw new InvalidParamException();
						Int32 maximize = (Int32)prop;
						_fastMode = (maximize == 0);
						_maxMode = (maximize >= 2);
						*/
                            break;
                        }
                    case CoderPropID.MatchFinder:
                        {
                            if (!(prop is String)) throw new InvalidParamException();
                            EMatchFinderType matchFinderIndexPrev = _matchFinderType;
                            int m = FindMatchFinder(((string) prop).ToUpper());
                            if (m < 0) throw new InvalidParamException();
                            _matchFinderType = (EMatchFinderType) m;
                            if (_matchFinder != null && matchFinderIndexPrev != _matchFinderType)
                            {
                                _dictionarySizePrev = 0xFFFFFFFF;
                                _matchFinder = null;
                            }
                            break;
                        }
                    case CoderPropID.DictionarySize:
                        {
                            const int kDicLogSizeMaxCompress = 30;
                            if (!(prop is Int32)) throw new InvalidParamException();
                            ;
                            var dictionarySize = (Int32) prop;
                            if (dictionarySize < (UInt32) (1 << Base.KDicLogSizeMin) || dictionarySize > (UInt32) (1 << kDicLogSizeMaxCompress)) throw new InvalidParamException();
                            _dictionarySize = (UInt32) dictionarySize;
                            int dicLogSize;
                            for (dicLogSize = 0; dicLogSize < (UInt32) kDicLogSizeMaxCompress; dicLogSize++) if (dictionarySize <= ((UInt32) (1) << dicLogSize)) break;
                            _distTableSize = (UInt32) dicLogSize*2;
                            break;
                        }
                    case CoderPropID.PosStateBits:
                        {
                            if (!(prop is Int32)) throw new InvalidParamException();
                            var v = (Int32) prop;
                            if (v < 0 || v > (UInt32) Base.KNumPosStatesBitsEncodingMax) throw new InvalidParamException();
                            _posStateBits = v;
                            _posStateMask = (((UInt32) 1) << _posStateBits) - 1;
                            break;
                        }
                    case CoderPropID.LitPosBits:
                        {
                            if (!(prop is Int32)) throw new InvalidParamException();
                            var v = (Int32) prop;
                            if (v < 0 || v > Base.KNumLitPosStatesBitsEncodingMax) throw new InvalidParamException();
                            _numLiteralPosStateBits = v;
                            break;
                        }
                    case CoderPropID.LitContextBits:
                        {
                            if (!(prop is Int32)) throw new InvalidParamException();
                            var v = (Int32) prop;
                            if (v < 0 || v > Base.KNumLitContextBitsMax) throw new InvalidParamException();
                            ;
                            _numLiteralContextBits = v;
                            break;
                        }
                    case CoderPropID.EndMarker:
                        {
                            if (!(prop is Boolean)) throw new InvalidParamException();
                            SetWriteEndMarkerMode((Boolean) prop);
                            break;
                        }
                    default:
                        throw new InvalidParamException();
                }
            }
        }

        #endregion

        #region IWriteCoderProperties Members

        public void WriteCoderProperties(Stream outStream)
        {
            _properties[0] = (Byte) ((_posStateBits*5 + _numLiteralPosStateBits)*9 + _numLiteralContextBits);
            for (int i = 0; i < 4; i++) _properties[1 + i] = (Byte) ((_dictionarySize >> (8*i)) & 0xFF);
            outStream.Write(_properties, 0, KPropSize);
        }

        #endregion

        private static UInt32 GetPosSlot(UInt32 pos)
        {
            if (pos < (1 << 11)) return GFastPos[pos];
            if (pos < (1 << 21)) return (UInt32) (GFastPos[pos >> 10] + 20);
            return (UInt32) (GFastPos[pos >> 20] + 40);
        }

        private static UInt32 GetPosSlot2(UInt32 pos)
        {
            if (pos < (1 << 17)) return (UInt32) (GFastPos[pos >> 6] + 12);
            if (pos < (1 << 27)) return (UInt32) (GFastPos[pos >> 16] + 32);
            return (UInt32) (GFastPos[pos >> 26] + 52);
        }

        private void BaseInit()
        {
            _state.Init();
            _previousByte = 0;
            for (UInt32 i = 0; i < Base.KNumRepDistances; i++) _repDistances[i] = 0;
        }

        private void Create()
        {
            if (_matchFinder == null)
            {
                var bt = new BinTree();
                int numHashBytes = 4;
                if (_matchFinderType == EMatchFinderType.BT2) numHashBytes = 2;
                bt.SetType(numHashBytes);
                _matchFinder = bt;
            }
            _literalEncoder.Create(_numLiteralPosStateBits, _numLiteralContextBits);

            if (_dictionarySize == _dictionarySizePrev && _numFastBytesPrev == _numFastBytes) return;
            _matchFinder.Create(_dictionarySize, KNumOpts, _numFastBytes, Base.KMatchMaxLen + 1);
            _dictionarySizePrev = _dictionarySize;
            _numFastBytesPrev = _numFastBytes;
        }

        private void SetWriteEndMarkerMode(bool writeEndMarker) { _writeEndMark = writeEndMarker; }

        private void Init()
        {
            BaseInit();
            _rangeEncoder.Init();

            uint i;
            for (i = 0; i < Base.KNumStates; i++)
            {
                for (uint j = 0; j <= _posStateMask; j++)
                {
                    uint complexState = (i << Base.KNumPosStatesBitsMax) + j;
                    _isMatch[complexState].Init();
                    _isRep0Long[complexState].Init();
                }
                _isRep[i].Init();
                _isRepG0[i].Init();
                _isRepG1[i].Init();
                _isRepG2[i].Init();
            }
            _literalEncoder.Init();
            for (i = 0; i < Base.KNumLenToPosStates; i++) _posSlotEncoder[i].Init();
            for (i = 0; i < Base.KNumFullDistances - Base.KEndPosModelIndex; i++) _posEncoders[i].Init();

            _lenEncoder.Init((UInt32) 1 << _posStateBits);
            _repMatchLenEncoder.Init((UInt32) 1 << _posStateBits);

            _posAlignEncoder.Init();

            _longestMatchWasFound = false;
            _optimumEndIndex = 0;
            _optimumCurrentIndex = 0;
            _additionalOffset = 0;
        }

        private void ReadMatchDistances(out UInt32 lenRes, out UInt32 numDistancePairs)
        {
            lenRes = 0;
            numDistancePairs = _matchFinder.GetMatches(_matchDistances);
            if (numDistancePairs > 0)
            {
                lenRes = _matchDistances[numDistancePairs - 2];
                if (lenRes == _numFastBytes) lenRes += _matchFinder.GetMatchLen((int) lenRes - 1, _matchDistances[numDistancePairs - 1], Base.KMatchMaxLen - lenRes);
            }
            _additionalOffset++;
        }


        private void MovePos(UInt32 num)
        {
            if (num > 0)
            {
                _matchFinder.Skip(num);
                _additionalOffset += num;
            }
        }

        private UInt32 GetRepLen1Price(Base.State state, UInt32 posState) { return _isRepG0[state.Index].GetPrice0() + _isRep0Long[(state.Index << Base.KNumPosStatesBitsMax) + posState].GetPrice0(); }

        private UInt32 GetPureRepPrice(UInt32 repIndex, Base.State state, UInt32 posState)
        {
            UInt32 price;
            if (repIndex == 0)
            {
                price = _isRepG0[state.Index].GetPrice0();
                price += _isRep0Long[(state.Index << Base.KNumPosStatesBitsMax) + posState].GetPrice1();
            }
            else
            {
                price = _isRepG0[state.Index].GetPrice1();
                if (repIndex == 1) price += _isRepG1[state.Index].GetPrice0();
                else
                {
                    price += _isRepG1[state.Index].GetPrice1();
                    price += _isRepG2[state.Index].GetPrice(repIndex - 2);
                }
            }
            return price;
        }

        private UInt32 GetRepPrice(UInt32 repIndex, UInt32 len, Base.State state, UInt32 posState)
        {
            UInt32 price = _repMatchLenEncoder.GetPrice(len - Base.KMatchMinLen, posState);
            return price + GetPureRepPrice(repIndex, state, posState);
        }

        private UInt32 GetPosLenPrice(UInt32 pos, UInt32 len, UInt32 posState)
        {
            UInt32 price;
            UInt32 lenToPosState = Base.GetLenToPosState(len);
            if (pos < Base.KNumFullDistances) price = _distancesPrices[(lenToPosState*Base.KNumFullDistances) + pos];
            else price = _posSlotPrices[(lenToPosState << Base.KNumPosSlotBits) + GetPosSlot2(pos)] + _alignPrices[pos & Base.KAlignMask];
            return price + _lenEncoder.GetPrice(len - Base.KMatchMinLen, posState);
        }

        private UInt32 Backward(out UInt32 backRes, UInt32 cur)
        {
            _optimumEndIndex = cur;
            UInt32 posMem = _optimum[cur].PosPrev;
            UInt32 backMem = _optimum[cur].BackPrev;
            do
            {
                if (_optimum[cur].Prev1IsChar)
                {
                    _optimum[posMem].MakeAsChar();
                    _optimum[posMem].PosPrev = posMem - 1;
                    if (_optimum[cur].Prev2)
                    {
                        _optimum[posMem - 1].Prev1IsChar = false;
                        _optimum[posMem - 1].PosPrev = _optimum[cur].PosPrev2;
                        _optimum[posMem - 1].BackPrev = _optimum[cur].BackPrev2;
                    }
                }
                UInt32 posPrev = posMem;
                UInt32 backCur = backMem;

                backMem = _optimum[posPrev].BackPrev;
                posMem = _optimum[posPrev].PosPrev;

                _optimum[posPrev].BackPrev = backCur;
                _optimum[posPrev].PosPrev = cur;
                cur = posPrev;
            } while (cur > 0);
            backRes = _optimum[0].BackPrev;
            _optimumCurrentIndex = _optimum[0].PosPrev;
            return _optimumCurrentIndex;
        }


        private UInt32 GetOptimum(UInt32 position, out UInt32 backRes)
        {
            if (_optimumEndIndex != _optimumCurrentIndex)
            {
                UInt32 lenRes = _optimum[_optimumCurrentIndex].PosPrev - _optimumCurrentIndex;
                backRes = _optimum[_optimumCurrentIndex].BackPrev;
                _optimumCurrentIndex = _optimum[_optimumCurrentIndex].PosPrev;
                return lenRes;
            }
            _optimumCurrentIndex = _optimumEndIndex = 0;

            UInt32 lenMain, numDistancePairs;
            if (!_longestMatchWasFound)
            {
                ReadMatchDistances(out lenMain, out numDistancePairs);
            }
            else
            {
                lenMain = _longestMatchLength;
                numDistancePairs = _numDistancePairs;
                _longestMatchWasFound = false;
            }

            UInt32 numAvailableBytes = _matchFinder.GetNumAvailableBytes() + 1;
            if (numAvailableBytes < 2)
            {
                backRes = 0xFFFFFFFF;
                return 1;
            }
            if (numAvailableBytes > Base.KMatchMaxLen) numAvailableBytes = Base.KMatchMaxLen;

            UInt32 repMaxIndex = 0;
            UInt32 i;
            for (i = 0; i < Base.KNumRepDistances; i++)
            {
                _reps[i] = _repDistances[i];
                _repLens[i] = _matchFinder.GetMatchLen(0 - 1, _reps[i], Base.KMatchMaxLen);
                if (_repLens[i] > _repLens[repMaxIndex]) repMaxIndex = i;
            }
            if (_repLens[repMaxIndex] >= _numFastBytes)
            {
                backRes = repMaxIndex;
                UInt32 lenRes = _repLens[repMaxIndex];
                MovePos(lenRes - 1);
                return lenRes;
            }

            if (lenMain >= _numFastBytes)
            {
                backRes = _matchDistances[numDistancePairs - 1] + Base.KNumRepDistances;
                MovePos(lenMain - 1);
                return lenMain;
            }

            Byte currentByte = _matchFinder.GetIndexByte(0 - 1);
            Byte matchByte = _matchFinder.GetIndexByte((Int32) (0 - _repDistances[0] - 1 - 1));

            if (lenMain < 2 && currentByte != matchByte && _repLens[repMaxIndex] < 2)
            {
                backRes = 0xFFFFFFFF;
                return 1;
            }

            _optimum[0].State = _state;

            UInt32 posState = (position & _posStateMask);

            _optimum[1].Price = _isMatch[(_state.Index << Base.KNumPosStatesBitsMax) + posState].GetPrice0() + _literalEncoder.GetSubCoder(position, _previousByte).GetPrice(!_state.IsCharState(), matchByte, currentByte);
            _optimum[1].MakeAsChar();

            UInt32 matchPrice = _isMatch[(_state.Index << Base.KNumPosStatesBitsMax) + posState].GetPrice1();
            UInt32 repMatchPrice = matchPrice + _isRep[_state.Index].GetPrice1();

            if (matchByte == currentByte)
            {
                UInt32 shortRepPrice = repMatchPrice + GetRepLen1Price(_state, posState);
                if (shortRepPrice < _optimum[1].Price)
                {
                    _optimum[1].Price = shortRepPrice;
                    _optimum[1].MakeAsShortRep();
                }
            }

            UInt32 lenEnd = ((lenMain >= _repLens[repMaxIndex]) ? lenMain : _repLens[repMaxIndex]);

            if (lenEnd < 2)
            {
                backRes = _optimum[1].BackPrev;
                return 1;
            }

            _optimum[1].PosPrev = 0;

            _optimum[0].Backs0 = _reps[0];
            _optimum[0].Backs1 = _reps[1];
            _optimum[0].Backs2 = _reps[2];
            _optimum[0].Backs3 = _reps[3];

            UInt32 len = lenEnd;
            do _optimum[len--].Price = KIfinityPrice; while (len >= 2);

            for (i = 0; i < Base.KNumRepDistances; i++)
            {
                UInt32 repLen = _repLens[i];
                if (repLen < 2) continue;
                UInt32 price = repMatchPrice + GetPureRepPrice(i, _state, posState);
                do
                {
                    UInt32 curAndLenPrice = price + _repMatchLenEncoder.GetPrice(repLen - 2, posState);
                    Optimal optimum = _optimum[repLen];
                    if (curAndLenPrice < optimum.Price)
                    {
                        optimum.Price = curAndLenPrice;
                        optimum.PosPrev = 0;
                        optimum.BackPrev = i;
                        optimum.Prev1IsChar = false;
                    }
                } while (--repLen >= 2);
            }

            UInt32 normalMatchPrice = matchPrice + _isRep[_state.Index].GetPrice0();

            len = ((_repLens[0] >= 2) ? _repLens[0] + 1 : 2);
            if (len <= lenMain)
            {
                UInt32 offs = 0;
                while (len > _matchDistances[offs]) offs += 2;
                for (;; len++)
                {
                    UInt32 distance = _matchDistances[offs + 1];
                    UInt32 curAndLenPrice = normalMatchPrice + GetPosLenPrice(distance, len, posState);
                    Optimal optimum = _optimum[len];
                    if (curAndLenPrice < optimum.Price)
                    {
                        optimum.Price = curAndLenPrice;
                        optimum.PosPrev = 0;
                        optimum.BackPrev = distance + Base.KNumRepDistances;
                        optimum.Prev1IsChar = false;
                    }
                    if (len == _matchDistances[offs])
                    {
                        offs += 2;
                        if (offs == numDistancePairs) break;
                    }
                }
            }

            UInt32 cur = 0;

            while (true)
            {
                cur++;
                if (cur == lenEnd) return Backward(out backRes, cur);
                UInt32 newLen;
                ReadMatchDistances(out newLen, out numDistancePairs);
                if (newLen >= _numFastBytes)
                {
                    _numDistancePairs = numDistancePairs;
                    _longestMatchLength = newLen;
                    _longestMatchWasFound = true;
                    return Backward(out backRes, cur);
                }
                position++;
                UInt32 posPrev = _optimum[cur].PosPrev;
                Base.State state;
                if (_optimum[cur].Prev1IsChar)
                {
                    posPrev--;
                    if (_optimum[cur].Prev2)
                    {
                        state = _optimum[_optimum[cur].PosPrev2].State;
                        if (_optimum[cur].BackPrev2 < Base.KNumRepDistances) state.UpdateRep();
                        else state.UpdateMatch();
                    }
                    else state = _optimum[posPrev].State;
                    state.UpdateChar();
                }
                else state = _optimum[posPrev].State;
                if (posPrev == cur - 1)
                {
                    if (_optimum[cur].IsShortRep()) state.UpdateShortRep();
                    else state.UpdateChar();
                }
                else
                {
                    UInt32 pos;
                    if (_optimum[cur].Prev1IsChar && _optimum[cur].Prev2)
                    {
                        posPrev = _optimum[cur].PosPrev2;
                        pos = _optimum[cur].BackPrev2;
                        state.UpdateRep();
                    }
                    else
                    {
                        pos = _optimum[cur].BackPrev;
                        if (pos < Base.KNumRepDistances) state.UpdateRep();
                        else state.UpdateMatch();
                    }
                    Optimal opt = _optimum[posPrev];
                    if (pos < Base.KNumRepDistances)
                    {
                        if (pos == 0)
                        {
                            _reps[0] = opt.Backs0;
                            _reps[1] = opt.Backs1;
                            _reps[2] = opt.Backs2;
                            _reps[3] = opt.Backs3;
                        }
                        else if (pos == 1)
                        {
                            _reps[0] = opt.Backs1;
                            _reps[1] = opt.Backs0;
                            _reps[2] = opt.Backs2;
                            _reps[3] = opt.Backs3;
                        }
                        else if (pos == 2)
                        {
                            _reps[0] = opt.Backs2;
                            _reps[1] = opt.Backs0;
                            _reps[2] = opt.Backs1;
                            _reps[3] = opt.Backs3;
                        }
                        else
                        {
                            _reps[0] = opt.Backs3;
                            _reps[1] = opt.Backs0;
                            _reps[2] = opt.Backs1;
                            _reps[3] = opt.Backs2;
                        }
                    }
                    else
                    {
                        _reps[0] = (pos - Base.KNumRepDistances);
                        _reps[1] = opt.Backs0;
                        _reps[2] = opt.Backs1;
                        _reps[3] = opt.Backs2;
                    }
                }
                _optimum[cur].State = state;
                _optimum[cur].Backs0 = _reps[0];
                _optimum[cur].Backs1 = _reps[1];
                _optimum[cur].Backs2 = _reps[2];
                _optimum[cur].Backs3 = _reps[3];
                UInt32 curPrice = _optimum[cur].Price;

                currentByte = _matchFinder.GetIndexByte(0 - 1);
                matchByte = _matchFinder.GetIndexByte((Int32) (0 - _reps[0] - 1 - 1));

                posState = (position & _posStateMask);

                UInt32 curAnd1Price = curPrice + _isMatch[(state.Index << Base.KNumPosStatesBitsMax) + posState].GetPrice0() + _literalEncoder.GetSubCoder(position, _matchFinder.GetIndexByte(0 - 2)).GetPrice(!state.IsCharState(), matchByte, currentByte);

                Optimal nextOptimum = _optimum[cur + 1];

                bool nextIsChar = false;
                if (curAnd1Price < nextOptimum.Price)
                {
                    nextOptimum.Price = curAnd1Price;
                    nextOptimum.PosPrev = cur;
                    nextOptimum.MakeAsChar();
                    nextIsChar = true;
                }

                matchPrice = curPrice + _isMatch[(state.Index << Base.KNumPosStatesBitsMax) + posState].GetPrice1();
                repMatchPrice = matchPrice + _isRep[state.Index].GetPrice1();

                if (matchByte == currentByte && !(nextOptimum.PosPrev < cur && nextOptimum.BackPrev == 0))
                {
                    UInt32 shortRepPrice = repMatchPrice + GetRepLen1Price(state, posState);
                    if (shortRepPrice <= nextOptimum.Price)
                    {
                        nextOptimum.Price = shortRepPrice;
                        nextOptimum.PosPrev = cur;
                        nextOptimum.MakeAsShortRep();
                        nextIsChar = true;
                    }
                }

                UInt32 numAvailableBytesFull = _matchFinder.GetNumAvailableBytes() + 1;
                numAvailableBytesFull = Math.Min(KNumOpts - 1 - cur, numAvailableBytesFull);
                numAvailableBytes = numAvailableBytesFull;

                if (numAvailableBytes < 2) continue;
                if (numAvailableBytes > _numFastBytes) numAvailableBytes = _numFastBytes;
                if (!nextIsChar && matchByte != currentByte)
                {
                    // try Literal + rep0
                    UInt32 t = Math.Min(numAvailableBytesFull - 1, _numFastBytes);
                    UInt32 lenTest2 = _matchFinder.GetMatchLen(0, _reps[0], t);
                    if (lenTest2 >= 2)
                    {
                        Base.State state2 = state;
                        state2.UpdateChar();
                        UInt32 posStateNext = (position + 1) & _posStateMask;
                        UInt32 nextRepMatchPrice = curAnd1Price + _isMatch[(state2.Index << Base.KNumPosStatesBitsMax) + posStateNext].GetPrice1() + _isRep[state2.Index].GetPrice1();
                        {
                            UInt32 offset = cur + 1 + lenTest2;
                            while (lenEnd < offset) _optimum[++lenEnd].Price = KIfinityPrice;
                            UInt32 curAndLenPrice = nextRepMatchPrice + GetRepPrice(0, lenTest2, state2, posStateNext);
                            Optimal optimum = _optimum[offset];
                            if (curAndLenPrice < optimum.Price)
                            {
                                optimum.Price = curAndLenPrice;
                                optimum.PosPrev = cur + 1;
                                optimum.BackPrev = 0;
                                optimum.Prev1IsChar = true;
                                optimum.Prev2 = false;
                            }
                        }
                    }
                }

                UInt32 startLen = 2; // speed optimization 

                for (UInt32 repIndex = 0; repIndex < Base.KNumRepDistances; repIndex++)
                {
                    UInt32 lenTest = _matchFinder.GetMatchLen(0 - 1, _reps[repIndex], numAvailableBytes);
                    if (lenTest < 2) continue;
                    UInt32 lenTestTemp = lenTest;
                    do
                    {
                        while (lenEnd < cur + lenTest) _optimum[++lenEnd].Price = KIfinityPrice;
                        UInt32 curAndLenPrice = repMatchPrice + GetRepPrice(repIndex, lenTest, state, posState);
                        Optimal optimum = _optimum[cur + lenTest];
                        if (curAndLenPrice < optimum.Price)
                        {
                            optimum.Price = curAndLenPrice;
                            optimum.PosPrev = cur;
                            optimum.BackPrev = repIndex;
                            optimum.Prev1IsChar = false;
                        }
                    } while (--lenTest >= 2);
                    lenTest = lenTestTemp;

                    if (repIndex == 0) startLen = lenTest + 1;

                    // if (_maxMode)
                    if (lenTest < numAvailableBytesFull)
                    {
                        UInt32 t = Math.Min(numAvailableBytesFull - 1 - lenTest, _numFastBytes);
                        UInt32 lenTest2 = _matchFinder.GetMatchLen((Int32) lenTest, _reps[repIndex], t);
                        if (lenTest2 >= 2)
                        {
                            Base.State state2 = state;
                            state2.UpdateRep();
                            UInt32 posStateNext = (position + lenTest) & _posStateMask;
                            UInt32 curAndLenCharPrice = repMatchPrice + GetRepPrice(repIndex, lenTest, state, posState) + _isMatch[(state2.Index << Base.KNumPosStatesBitsMax) + posStateNext].GetPrice0() + _literalEncoder.GetSubCoder(position + lenTest, _matchFinder.GetIndexByte((Int32) lenTest - 1 - 1)).GetPrice(true, _matchFinder.GetIndexByte(((Int32) lenTest - 1 - (Int32) (_reps[repIndex] + 1))), _matchFinder.GetIndexByte((Int32) lenTest - 1));
                            state2.UpdateChar();
                            posStateNext = (position + lenTest + 1) & _posStateMask;
                            UInt32 nextMatchPrice = curAndLenCharPrice + _isMatch[(state2.Index << Base.KNumPosStatesBitsMax) + posStateNext].GetPrice1();
                            UInt32 nextRepMatchPrice = nextMatchPrice + _isRep[state2.Index].GetPrice1();

                            // for(; lenTest2 >= 2; lenTest2--)
                            {
                                UInt32 offset = lenTest + 1 + lenTest2;
                                while (lenEnd < cur + offset) _optimum[++lenEnd].Price = KIfinityPrice;
                                UInt32 curAndLenPrice = nextRepMatchPrice + GetRepPrice(0, lenTest2, state2, posStateNext);
                                Optimal optimum = _optimum[cur + offset];
                                if (curAndLenPrice < optimum.Price)
                                {
                                    optimum.Price = curAndLenPrice;
                                    optimum.PosPrev = cur + lenTest + 1;
                                    optimum.BackPrev = 0;
                                    optimum.Prev1IsChar = true;
                                    optimum.Prev2 = true;
                                    optimum.PosPrev2 = cur;
                                    optimum.BackPrev2 = repIndex;
                                }
                            }
                        }
                    }
                }

                if (newLen > numAvailableBytes)
                {
                    newLen = numAvailableBytes;
                    for (numDistancePairs = 0; newLen > _matchDistances[numDistancePairs]; numDistancePairs += 2)
                    {
                    }
                    _matchDistances[numDistancePairs] = newLen;
                    numDistancePairs += 2;
                }
                if (newLen >= startLen)
                {
                    normalMatchPrice = matchPrice + _isRep[state.Index].GetPrice0();
                    while (lenEnd < cur + newLen) _optimum[++lenEnd].Price = KIfinityPrice;

                    UInt32 offs = 0;
                    while (startLen > _matchDistances[offs]) offs += 2;

                    for (UInt32 lenTest = startLen;; lenTest++)
                    {
                        UInt32 curBack = _matchDistances[offs + 1];
                        UInt32 curAndLenPrice = normalMatchPrice + GetPosLenPrice(curBack, lenTest, posState);
                        Optimal optimum = _optimum[cur + lenTest];
                        if (curAndLenPrice < optimum.Price)
                        {
                            optimum.Price = curAndLenPrice;
                            optimum.PosPrev = cur;
                            optimum.BackPrev = curBack + Base.KNumRepDistances;
                            optimum.Prev1IsChar = false;
                        }

                        if (lenTest == _matchDistances[offs])
                        {
                            if (lenTest < numAvailableBytesFull)
                            {
                                UInt32 t = Math.Min(numAvailableBytesFull - 1 - lenTest, _numFastBytes);
                                UInt32 lenTest2 = _matchFinder.GetMatchLen((Int32) lenTest, curBack, t);
                                if (lenTest2 >= 2)
                                {
                                    Base.State state2 = state;
                                    state2.UpdateMatch();
                                    UInt32 posStateNext = (position + lenTest) & _posStateMask;
                                    UInt32 curAndLenCharPrice = curAndLenPrice + _isMatch[(state2.Index << Base.KNumPosStatesBitsMax) + posStateNext].GetPrice0() + _literalEncoder.GetSubCoder(position + lenTest, _matchFinder.GetIndexByte((Int32) lenTest - 1 - 1)).GetPrice(true, _matchFinder.GetIndexByte((Int32) lenTest - (Int32) (curBack + 1) - 1), _matchFinder.GetIndexByte((Int32) lenTest - 1));
                                    state2.UpdateChar();
                                    posStateNext = (position + lenTest + 1) & _posStateMask;
                                    UInt32 nextMatchPrice = curAndLenCharPrice + _isMatch[(state2.Index << Base.KNumPosStatesBitsMax) + posStateNext].GetPrice1();
                                    UInt32 nextRepMatchPrice = nextMatchPrice + _isRep[state2.Index].GetPrice1();

                                    UInt32 offset = lenTest + 1 + lenTest2;
                                    while (lenEnd < cur + offset) _optimum[++lenEnd].Price = KIfinityPrice;
                                    curAndLenPrice = nextRepMatchPrice + GetRepPrice(0, lenTest2, state2, posStateNext);
                                    optimum = _optimum[cur + offset];
                                    if (curAndLenPrice < optimum.Price)
                                    {
                                        optimum.Price = curAndLenPrice;
                                        optimum.PosPrev = cur + lenTest + 1;
                                        optimum.BackPrev = 0;
                                        optimum.Prev1IsChar = true;
                                        optimum.Prev2 = true;
                                        optimum.PosPrev2 = cur;
                                        optimum.BackPrev2 = curBack + Base.KNumRepDistances;
                                    }
                                }
                            }
                            offs += 2;
                            if (offs == numDistancePairs) break;
                        }
                    }
                }
            }
        }

        private bool ChangePair(UInt32 smallDist, UInt32 bigDist)
        {
            const int kDif = 7;
            return (smallDist < ((UInt32) (1) << (32 - kDif)) && bigDist >= (smallDist << kDif));
        }

        private void WriteEndMarker(UInt32 posState)
        {
            if (!_writeEndMark) return;

            _isMatch[(_state.Index << Base.KNumPosStatesBitsMax) + posState].Encode(_rangeEncoder, 1);
            _isRep[_state.Index].Encode(_rangeEncoder, 0);
            _state.UpdateMatch();
            UInt32 len = Base.KMatchMinLen;
            _lenEncoder.Encode(_rangeEncoder, len - Base.KMatchMinLen, posState);
            UInt32 posSlot = (1 << Base.KNumPosSlotBits) - 1;
            UInt32 lenToPosState = Base.GetLenToPosState(len);
            _posSlotEncoder[lenToPosState].Encode(_rangeEncoder, posSlot);
            int footerBits = 30;
            UInt32 posReduced = (((UInt32) 1) << footerBits) - 1;
            _rangeEncoder.EncodeDirectBits(posReduced >> Base.KNumAlignBits, footerBits - Base.KNumAlignBits);
            _posAlignEncoder.ReverseEncode(_rangeEncoder, posReduced & Base.KAlignMask);
        }

        private void Flush(UInt32 nowPos)
        {
            ReleaseMFStream();
            WriteEndMarker(nowPos & _posStateMask);
            _rangeEncoder.FlushData();
            _rangeEncoder.FlushStream();
        }

        public void CodeOneBlock(out Int64 inSize, out Int64 outSize, out bool finished)
        {
            inSize = 0;
            outSize = 0;
            finished = true;

            if (_inStream != null)
            {
                _matchFinder.SetStream(_inStream);
                _matchFinder.Init();
                _needReleaseMFStream = true;
                _inStream = null;
                if (_trainSize > 0) _matchFinder.Skip(_trainSize);
            }

            if (_finished) return;
            _finished = true;


            Int64 progressPosValuePrev = _nowPos64;
            if (_nowPos64 == 0)
            {
                if (_matchFinder.GetNumAvailableBytes() == 0)
                {
                    Flush((UInt32) _nowPos64);
                    return;
                }
                UInt32 len, numDistancePairs; // it's not used
                ReadMatchDistances(out len, out numDistancePairs);
                UInt32 posState = (UInt32) (_nowPos64) & _posStateMask;
                _isMatch[(_state.Index << Base.KNumPosStatesBitsMax) + posState].Encode(_rangeEncoder, 0);
                _state.UpdateChar();
                Byte curByte = _matchFinder.GetIndexByte((Int32) (0 - _additionalOffset));
                _literalEncoder.GetSubCoder((UInt32) (_nowPos64), _previousByte).Encode(_rangeEncoder, curByte);
                _previousByte = curByte;
                _additionalOffset--;
                _nowPos64++;
            }
            if (_matchFinder.GetNumAvailableBytes() == 0)
            {
                Flush((UInt32) _nowPos64);
                return;
            }
            while (true)
            {
                UInt32 pos;
                UInt32 len = GetOptimum((UInt32) _nowPos64, out pos);

                UInt32 posState = ((UInt32) _nowPos64) & _posStateMask;
                UInt32 complexState = (_state.Index << Base.KNumPosStatesBitsMax) + posState;
                if (len == 1 && pos == 0xFFFFFFFF)
                {
                    _isMatch[complexState].Encode(_rangeEncoder, 0);
                    Byte curByte = _matchFinder.GetIndexByte((Int32) (0 - _additionalOffset));
                    LiteralEncoder.Encoder2 subCoder = _literalEncoder.GetSubCoder((UInt32) _nowPos64, _previousByte);
                    if (!_state.IsCharState())
                    {
                        Byte matchByte = _matchFinder.GetIndexByte((Int32) (0 - _repDistances[0] - 1 - _additionalOffset));
                        subCoder.EncodeMatched(_rangeEncoder, matchByte, curByte);
                    }
                    else subCoder.Encode(_rangeEncoder, curByte);
                    _previousByte = curByte;
                    _state.UpdateChar();
                }
                else
                {
                    _isMatch[complexState].Encode(_rangeEncoder, 1);
                    if (pos < Base.KNumRepDistances)
                    {
                        _isRep[_state.Index].Encode(_rangeEncoder, 1);
                        if (pos == 0)
                        {
                            _isRepG0[_state.Index].Encode(_rangeEncoder, 0);
                            if (len == 1) _isRep0Long[complexState].Encode(_rangeEncoder, 0);
                            else _isRep0Long[complexState].Encode(_rangeEncoder, 1);
                        }
                        else
                        {
                            _isRepG0[_state.Index].Encode(_rangeEncoder, 1);
                            if (pos == 1) _isRepG1[_state.Index].Encode(_rangeEncoder, 0);
                            else
                            {
                                _isRepG1[_state.Index].Encode(_rangeEncoder, 1);
                                _isRepG2[_state.Index].Encode(_rangeEncoder, pos - 2);
                            }
                        }
                        if (len == 1) _state.UpdateShortRep();
                        else
                        {
                            _repMatchLenEncoder.Encode(_rangeEncoder, len - Base.KMatchMinLen, posState);
                            _state.UpdateRep();
                        }
                        UInt32 distance = _repDistances[pos];
                        if (pos != 0)
                        {
                            for (UInt32 i = pos; i >= 1; i--) _repDistances[i] = _repDistances[i - 1];
                            _repDistances[0] = distance;
                        }
                    }
                    else
                    {
                        _isRep[_state.Index].Encode(_rangeEncoder, 0);
                        _state.UpdateMatch();
                        _lenEncoder.Encode(_rangeEncoder, len - Base.KMatchMinLen, posState);
                        pos -= Base.KNumRepDistances;
                        UInt32 posSlot = GetPosSlot(pos);
                        UInt32 lenToPosState = Base.GetLenToPosState(len);
                        _posSlotEncoder[lenToPosState].Encode(_rangeEncoder, posSlot);

                        if (posSlot >= Base.KStartPosModelIndex)
                        {
                            var footerBits = (int) ((posSlot >> 1) - 1);
                            UInt32 baseVal = ((2 | (posSlot & 1)) << footerBits);
                            UInt32 posReduced = pos - baseVal;

                            if (posSlot < Base.KEndPosModelIndex) BitTreeEncoder.ReverseEncode(_posEncoders, baseVal - posSlot - 1, _rangeEncoder, footerBits, posReduced);
                            else
                            {
                                _rangeEncoder.EncodeDirectBits(posReduced >> Base.KNumAlignBits, footerBits - Base.KNumAlignBits);
                                _posAlignEncoder.ReverseEncode(_rangeEncoder, posReduced & Base.KAlignMask);
                                _alignPriceCount++;
                            }
                        }
                        UInt32 distance = pos;
                        for (UInt32 i = Base.KNumRepDistances - 1; i >= 1; i--) _repDistances[i] = _repDistances[i - 1];
                        _repDistances[0] = distance;
                        _matchPriceCount++;
                    }
                    _previousByte = _matchFinder.GetIndexByte((Int32) (len - 1 - _additionalOffset));
                }
                _additionalOffset -= len;
                _nowPos64 += len;
                if (_additionalOffset == 0)
                {
                    // if (!_fastMode)
                    if (_matchPriceCount >= (1 << 7)) FillDistancesPrices();
                    if (_alignPriceCount >= Base.KAlignTableSize) FillAlignPrices();
                    inSize = _nowPos64;
                    outSize = _rangeEncoder.GetProcessedSizeAdd();
                    if (_matchFinder.GetNumAvailableBytes() == 0)
                    {
                        Flush((UInt32) _nowPos64);
                        return;
                    }

                    if (_nowPos64 - progressPosValuePrev >= (1 << 12))
                    {
                        _finished = false;
                        finished = false;
                        return;
                    }
                }
            }
        }

        private void ReleaseMFStream()
        {
            if (_matchFinder != null && _needReleaseMFStream)
            {
                _matchFinder.ReleaseStream();
                _needReleaseMFStream = false;
            }
        }

        private void SetOutStream(Stream outStream) { _rangeEncoder.SetStream(outStream); }
        private void ReleaseOutStream() { _rangeEncoder.ReleaseStream(); }

        private void ReleaseStreams()
        {
            ReleaseMFStream();
            ReleaseOutStream();
        }

        private void SetStreams(Stream inStream, Stream outStream, Int64 inSize, Int64 outSize)
        {
            _inStream = inStream;
            _finished = false;
            Create();
            SetOutStream(outStream);
            Init();

            // if (!_fastMode)
            {
                FillDistancesPrices();
                FillAlignPrices();
            }

            _lenEncoder.SetTableSize(_numFastBytes + 1 - Base.KMatchMinLen);
            _lenEncoder.UpdateTables((UInt32) 1 << _posStateBits);
            _repMatchLenEncoder.SetTableSize(_numFastBytes + 1 - Base.KMatchMinLen);
            _repMatchLenEncoder.UpdateTables((UInt32) 1 << _posStateBits);

            _nowPos64 = 0;
        }


        private void FillDistancesPrices()
        {
            for (UInt32 i = Base.KStartPosModelIndex; i < Base.KNumFullDistances; i++)
            {
                UInt32 posSlot = GetPosSlot(i);
                var footerBits = (int) ((posSlot >> 1) - 1);
                UInt32 baseVal = ((2 | (posSlot & 1)) << footerBits);
                _tempPrices[i] = BitTreeEncoder.ReverseGetPrice(_posEncoders, baseVal - posSlot - 1, footerBits, i - baseVal);
            }

            for (UInt32 lenToPosState = 0; lenToPosState < Base.KNumLenToPosStates; lenToPosState++)
            {
                UInt32 posSlot;
                BitTreeEncoder encoder = _posSlotEncoder[lenToPosState];

                UInt32 st = (lenToPosState << Base.KNumPosSlotBits);
                for (posSlot = 0; posSlot < _distTableSize; posSlot++) _posSlotPrices[st + posSlot] = encoder.GetPrice(posSlot);
                for (posSlot = Base.KEndPosModelIndex; posSlot < _distTableSize; posSlot++) _posSlotPrices[st + posSlot] += ((((posSlot >> 1) - 1) - Base.KNumAlignBits) << BitEncoder.KNumBitPriceShiftBits);

                UInt32 st2 = lenToPosState*Base.KNumFullDistances;
                UInt32 i;
                for (i = 0; i < Base.KStartPosModelIndex; i++) _distancesPrices[st2 + i] = _posSlotPrices[st + i];
                for (; i < Base.KNumFullDistances; i++) _distancesPrices[st2 + i] = _posSlotPrices[st + GetPosSlot(i)] + _tempPrices[i];
            }
            _matchPriceCount = 0;
        }

        private void FillAlignPrices()
        {
            for (UInt32 i = 0; i < Base.KAlignTableSize; i++) _alignPrices[i] = _posAlignEncoder.ReverseGetPrice(i);
            _alignPriceCount = 0;
        }


        private static int FindMatchFinder(string s)
        {
            for (int m = 0; m < KMatchFinderIDs.Length; m++) if (s == KMatchFinderIDs[m]) return m;
            return -1;
        }

        public void SetTrainSize(uint trainSize) { _trainSize = trainSize; }

        #region Nested type: EMatchFinderType

        private enum EMatchFinderType
        {
            BT2,
            BT4,
        };

        #endregion

        #region Nested type: LenEncoder

        private class LenEncoder
        {
            private readonly BitTreeEncoder[] _lowCoder = new BitTreeEncoder[Base.KNumPosStatesEncodingMax];
            private readonly BitTreeEncoder[] _midCoder = new BitTreeEncoder[Base.KNumPosStatesEncodingMax];
            private BitEncoder _choice;
            private BitEncoder _choice2;
            private BitTreeEncoder _highCoder = new BitTreeEncoder(Base.KNumHighLenBits);

            public LenEncoder()
            {
                for (UInt32 posState = 0; posState < Base.KNumPosStatesEncodingMax; posState++)
                {
                    _lowCoder[posState] = new BitTreeEncoder(Base.KNumLowLenBits);
                    _midCoder[posState] = new BitTreeEncoder(Base.KNumMidLenBits);
                }
            }

            public void Init(UInt32 numPosStates)
            {
                _choice.Init();
                _choice2.Init();
                for (UInt32 posState = 0; posState < numPosStates; posState++)
                {
                    _lowCoder[posState].Init();
                    _midCoder[posState].Init();
                }
                _highCoder.Init();
            }

            public void Encode(RangeCoder.Encoder rangeEncoder, UInt32 symbol, UInt32 posState)
            {
                if (symbol < Base.KNumLowLenSymbols)
                {
                    _choice.Encode(rangeEncoder, 0);
                    _lowCoder[posState].Encode(rangeEncoder, symbol);
                }
                else
                {
                    symbol -= Base.KNumLowLenSymbols;
                    _choice.Encode(rangeEncoder, 1);
                    if (symbol < Base.KNumMidLenSymbols)
                    {
                        _choice2.Encode(rangeEncoder, 0);
                        _midCoder[posState].Encode(rangeEncoder, symbol);
                    }
                    else
                    {
                        _choice2.Encode(rangeEncoder, 1);
                        _highCoder.Encode(rangeEncoder, symbol - Base.KNumMidLenSymbols);
                    }
                }
            }

            public void SetPrices(UInt32 posState, UInt32 numSymbols, UInt32[] prices, UInt32 st)
            {
                UInt32 a0 = _choice.GetPrice0();
                UInt32 a1 = _choice.GetPrice1();
                UInt32 b0 = a1 + _choice2.GetPrice0();
                UInt32 b1 = a1 + _choice2.GetPrice1();
                UInt32 i = 0;
                for (i = 0; i < Base.KNumLowLenSymbols; i++)
                {
                    if (i >= numSymbols) return;
                    prices[st + i] = a0 + _lowCoder[posState].GetPrice(i);
                }
                for (; i < Base.KNumLowLenSymbols + Base.KNumMidLenSymbols; i++)
                {
                    if (i >= numSymbols) return;
                    prices[st + i] = b0 + _midCoder[posState].GetPrice(i - Base.KNumLowLenSymbols);
                }
                for (; i < numSymbols; i++) prices[st + i] = b1 + _highCoder.GetPrice(i - Base.KNumLowLenSymbols - Base.KNumMidLenSymbols);
            }
        };

        #endregion

        #region Nested type: LenPriceTableEncoder

        private class LenPriceTableEncoder : LenEncoder
        {
            private readonly UInt32[] _counters = new UInt32[Base.KNumPosStatesEncodingMax];
            private readonly UInt32[] _prices = new UInt32[Base.KNumLenSymbols << Base.KNumPosStatesBitsEncodingMax];
            private UInt32 _tableSize;

            public void SetTableSize(UInt32 tableSize) { _tableSize = tableSize; }

            public UInt32 GetPrice(UInt32 symbol, UInt32 posState) { return _prices[posState*Base.KNumLenSymbols + symbol]; }

            private void UpdateTable(UInt32 posState)
            {
                SetPrices(posState, _tableSize, _prices, posState*Base.KNumLenSymbols);
                _counters[posState] = _tableSize;
            }

            public void UpdateTables(UInt32 numPosStates) { for (UInt32 posState = 0; posState < numPosStates; posState++) UpdateTable(posState); }

            public new void Encode(RangeCoder.Encoder rangeEncoder, UInt32 symbol, UInt32 posState)
            {
                base.Encode(rangeEncoder, symbol, posState);
                if (--_counters[posState] == 0) UpdateTable(posState);
            }
        }

        #endregion

        #region Nested type: LiteralEncoder

        private class LiteralEncoder
        {
            private Encoder2[] m_Coders;
            private int m_NumPosBits;
            private int m_NumPrevBits;
            private uint m_PosMask;

            public void Create(int numPosBits, int numPrevBits)
            {
                if (m_Coders != null && m_NumPrevBits == numPrevBits && m_NumPosBits == numPosBits) return;
                m_NumPosBits = numPosBits;
                m_PosMask = ((uint) 1 << numPosBits) - 1;
                m_NumPrevBits = numPrevBits;
                uint numStates = (uint) 1 << (m_NumPrevBits + m_NumPosBits);
                m_Coders = new Encoder2[numStates];
                for (uint i = 0; i < numStates; i++) m_Coders[i].Create();
            }

            public void Init()
            {
                uint numStates = (uint) 1 << (m_NumPrevBits + m_NumPosBits);
                for (uint i = 0; i < numStates; i++) m_Coders[i].Init();
            }

            public Encoder2 GetSubCoder(UInt32 pos, Byte prevByte) { return m_Coders[((pos & m_PosMask) << m_NumPrevBits) + (uint) (prevByte >> (8 - m_NumPrevBits))]; }

            #region Nested type: Encoder2

            public struct Encoder2
            {
                private BitEncoder[] m_Encoders;

                public void Create() { m_Encoders = new BitEncoder[0x300]; }

                public void Init() { for (int i = 0; i < 0x300; i++) m_Encoders[i].Init(); }

                public void Encode(RangeCoder.Encoder rangeEncoder, byte symbol)
                {
                    uint context = 1;
                    for (int i = 7; i >= 0; i--)
                    {
                        var bit = (uint) ((symbol >> i) & 1);
                        m_Encoders[context].Encode(rangeEncoder, bit);
                        context = (context << 1) | bit;
                    }
                }

                public void EncodeMatched(RangeCoder.Encoder rangeEncoder, byte matchByte, byte symbol)
                {
                    uint context = 1;
                    bool same = true;
                    for (int i = 7; i >= 0; i--)
                    {
                        var bit = (uint) ((symbol >> i) & 1);
                        uint state = context;
                        if (same)
                        {
                            var matchBit = (uint) ((matchByte >> i) & 1);
                            state += ((1 + matchBit) << 8);
                            same = (matchBit == bit);
                        }
                        m_Encoders[state].Encode(rangeEncoder, bit);
                        context = (context << 1) | bit;
                    }
                }

                public uint GetPrice(bool matchMode, byte matchByte, byte symbol)
                {
                    uint price = 0;
                    uint context = 1;
                    int i = 7;
                    if (matchMode)
                    {
                        for (; i >= 0; i--)
                        {
                            uint matchBit = (uint) (matchByte >> i) & 1;
                            uint bit = (uint) (symbol >> i) & 1;
                            price += m_Encoders[((1 + matchBit) << 8) + context].GetPrice(bit);
                            context = (context << 1) | bit;
                            if (matchBit != bit)
                            {
                                i--;
                                break;
                            }
                        }
                    }
                    for (; i >= 0; i--)
                    {
                        uint bit = (uint) (symbol >> i) & 1;
                        price += m_Encoders[context].GetPrice(bit);
                        context = (context << 1) | bit;
                    }
                    return price;
                }
            }

            #endregion
        }

        #endregion

        #region Nested type: Optimal

        private class Optimal
        {
            public UInt32 BackPrev;
            public UInt32 BackPrev2;

            public UInt32 Backs0;
            public UInt32 Backs1;
            public UInt32 Backs2;
            public UInt32 Backs3;
            public UInt32 PosPrev;
            public UInt32 PosPrev2;
            public bool Prev1IsChar;
            public bool Prev2;
            public UInt32 Price;
            public Base.State State;

            public void MakeAsChar()
            {
                BackPrev = 0xFFFFFFFF;
                Prev1IsChar = false;
            }

            public void MakeAsShortRep()
            {
                BackPrev = 0;
                ;
                Prev1IsChar = false;
            }

            public bool IsShortRep() { return (BackPrev == 0); }
        };

        #endregion
    }
}