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
using System.Collections.Generic;
using System.Linq;
using SevenZip;

namespace SebScheduler.Compression
{
    public class LzmaProperties
    {
        private readonly Dictionary<CoderPropID, object> _properties = new Dictionary<CoderPropID, object>();

        public CoderPropID[] Identifiers
        {
            get
            {
                if (!_properties.ContainsKey(CoderPropID.DictionarySize)) _properties.Add(CoderPropID.DictionarySize, 1 << 23);
                if (!_properties.ContainsKey(CoderPropID.PosStateBits)) _properties.Add(CoderPropID.PosStateBits, 2);
                if (!_properties.ContainsKey(CoderPropID.LitContextBits)) _properties.Add(CoderPropID.LitContextBits, 3);
                if (!_properties.ContainsKey(CoderPropID.LitPosBits)) _properties.Add(CoderPropID.LitPosBits, 0);
                if (!_properties.ContainsKey(CoderPropID.Algorithm)) _properties.Add(CoderPropID.Algorithm, 2);
                if (!_properties.ContainsKey(CoderPropID.NumFastBytes)) _properties.Add(CoderPropID.NumFastBytes, 128);
                if (!_properties.ContainsKey(CoderPropID.MatchFinder)) _properties.Add(CoderPropID.MatchFinder, "bt4");
                if (!_properties.ContainsKey(CoderPropID.EndMarker)) _properties.Add(CoderPropID.EndMarker, false);
                return _properties.Keys.ToArray();
            }
        }

        public object[] Values
        {
            get
            {
                var values = new List<object>();
                foreach (var id in Identifiers)
                {
                    switch (id)
                    {
                        case CoderPropID.DictionarySize:
                            if ((int)_properties[id] < 1)
                            {
                               values.Add(1); 
                            }
                            else if ((int)_properties[id] > 29)
                            {
                                values.Add(1 << 29);
                            }
                            else
                            {
                                values.Add(1 << ((int)_properties[id]));
                            }
                            break;
                        case CoderPropID.PosStateBits:
                        case CoderPropID.LitPosBits:
                            if ((int)_properties[id] < 0)
                            {
                                values.Add(0);
                            }
                            else if ((int)_properties[id] > 4)
                            {
                                values.Add(4);
                            }
                            else
                            {
                                values.Add((int)_properties[id]);
                            }
                            break;
                        case CoderPropID.LitContextBits:
                            if ((int)_properties[id] < 0)
                            {
                                values.Add(0);
                            }
                            else if ((int)_properties[id] > 8)
                            {
                                values.Add(8);
                            }
                            else
                            {
                                values.Add((int)_properties[id]);
                            }
                            break;
                        case CoderPropID.NumFastBytes:
                            if ((int)_properties[id] < 5)
                            {
                                values.Add(5);
                            }
                            else if ((int)_properties[id] > 273)
                            {
                                values.Add(273);
                            }
                            else
                            {
                                values.Add((int)_properties[id]);
                            }
                            break;
                        case CoderPropID.MatchFinder:
                            values.Add(_properties[id].ToString());
                            break;
                        default:
                            values.Add(_properties[id]);
                            break;
                    }
                }
                return values.ToArray();
            }
        }

        /// <summary>
        /// Specifies size of dictionary.
        /// </summary>
        public Property<int> DictionarySize { get { return new Property<int>(_properties, CoderPropID.DictionarySize); } }
        /// <summary>
        /// Specifies number of postion state bits for LZMA (0 &lt;= x &lt;= 4).
        /// </summary>
        public Property<int> PosStateBits { get { return new Property<int>(_properties, CoderPropID.PosStateBits); } }
        /// <summary>
        /// Specifies number of literal context bits for LZMA (0 &lt;= x &lt;= 8).
        /// </summary>
        public Property<int> LitContextBits { get { return new Property<int>(_properties, CoderPropID.LitContextBits); } }
        /// <summary>
        /// Specifies number of literal position bits for LZMA (0 &lt;= x &lt;= 4).
        /// </summary>
        public Property<int> LitPosBits { get { return new Property<int>(_properties, CoderPropID.LitPosBits); } }
        /// <summary>
        /// Specifies number of fast bytes for LZ*.
        /// </summary>
        public Property<int> NumFastBytes { get { return new Property<int>(_properties, CoderPropID.NumFastBytes); } }
        /// <summary>
        /// Specifies match finder. LZMA: "BT2" or "BT4".
        /// </summary>
        public Property<LzmaMatchFinderType> MatchFinder { get { return new Property<LzmaMatchFinderType>(_properties, CoderPropID.MatchFinder); } }
        /// <summary>
        /// Specifies mode with end marker.
        /// </summary>
        public Property<bool> EndMarker { get { return new Property<bool>(_properties, CoderPropID.EndMarker); } }

        public sealed class Property<T>
        {
            private readonly Dictionary<CoderPropID, object> _properties;
            private readonly CoderPropID _propID;

            internal Property(Dictionary<CoderPropID, object> properties, CoderPropID propID)
            {
                _properties = properties;
                _propID = propID;
            }

            public void Set(T value)
            {
                if (_properties.ContainsKey(CoderPropID.NumFastBytes))
                {
                    _properties[_propID] = value;
                }
                else
                {
                    _properties.Add(_propID, value);
                }
            }
            public void Clear()
            {
                if (!_properties.ContainsKey(_propID)) return;
                _properties.Remove(_propID);
            }
        }
    }
}