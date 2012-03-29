#region Notice
// This code is taken from version 9.20 of the LZMA SDK.
// Some modifications have been made byte Jens Granlund

// LZMA SDK is written and placed in the public domain by Igor Pavlov.

// Some code in LZMA SDK is based on public domain code from another developers:
//   1) PPMd var.H (2001): Dmitry Shkarin
//   2) SHA-256: Wei Dai (Crypto++ library)
#endregion

using System.IO;

namespace SevenZip.LZ
{
    public class OutWindow
    {
        public uint TrainSize;
        private byte[] _buffer;
        private uint _pos;
        private Stream _stream;
        private uint _streamPos;
        private uint _windowSize;

        public void Create(uint windowSize)
        {
            if (_windowSize != windowSize)
            {
                // System.GC.Collect();
                _buffer = new byte[windowSize];
            }
            _windowSize = windowSize;
            _pos = 0;
            _streamPos = 0;
        }

        public void Init(Stream stream, bool solid)
        {
            ReleaseStream();
            _stream = stream;
            if (!solid)
            {
                _streamPos = 0;
                _pos = 0;
                TrainSize = 0;
            }
        }

        public bool Train(Stream stream)
        {
            long len = stream.Length;
            uint size = (len < _windowSize) ? (uint) len : _windowSize;
            TrainSize = size;
            stream.Position = len - size;
            _streamPos = _pos = 0;
            while (size > 0)
            {
                uint curSize = _windowSize - _pos;
                if (size < curSize) curSize = size;
                int numReadBytes = stream.Read(_buffer, (int) _pos, (int) curSize);
                if (numReadBytes == 0) return false;
                size -= (uint) numReadBytes;
                _pos += (uint) numReadBytes;
                _streamPos += (uint) numReadBytes;
                if (_pos == _windowSize) _streamPos = _pos = 0;
            }
            return true;
        }

        public void ReleaseStream()
        {
            Flush();
            _stream = null;
        }

        public void Flush()
        {
            uint size = _pos - _streamPos;
            if (size == 0) return;
            _stream.Write(_buffer, (int) _streamPos, (int) size);
            if (_pos >= _windowSize) _pos = 0;
            _streamPos = _pos;
        }

        public void CopyBlock(uint distance, uint len)
        {
            uint pos = _pos - distance - 1;
            if (pos >= _windowSize) pos += _windowSize;
            for (; len > 0; len--)
            {
                if (pos >= _windowSize) pos = 0;
                _buffer[_pos++] = _buffer[pos++];
                if (_pos >= _windowSize) Flush();
            }
        }

        public void PutByte(byte b)
        {
            _buffer[_pos++] = b;
            if (_pos >= _windowSize) Flush();
        }

        public byte GetByte(uint distance)
        {
            uint pos = _pos - distance - 1;
            if (pos >= _windowSize) pos += _windowSize;
            return _buffer[pos];
        }
    }
}