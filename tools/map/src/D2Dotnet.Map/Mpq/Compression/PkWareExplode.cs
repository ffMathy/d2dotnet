namespace D2Dotnet.Map.Mpq.Compression;

/// <summary>
/// PKWARE Data Compression Library "explode" (decompression of imploded data).
/// Port of the <c>implode-decoder@1.0.1</c> npm package (MIT, (c) 2014 Travis Collins) used by upstream,
/// reduced to the way upstream drives it: a single input block followed by a flush.
/// </summary>
public static class PkWareExplode
{
    private const int CtBinary = 0;
    private const int CtAscii = 1;
    private const int EndMarker = 0x10E;

    private enum State
    {
        Header = 0,
        Decode = 1,
        RepeatedLiteral = 2,
        RepeatedExtraLength = 3,
        BinaryLiteral = 4,
        AsciiLiteral = 5,
        AsciiInterpretResult = 6,
        InterpretRepeatedLiteral = 7,
        GetDistanceBits = 8,
        WriteRepeatedLiteral = 9,
        WriteSingleLiteral = 10,
        Terminated = 100,
        Error = 666,
    }

    /// <summary>
    /// Decompress a block of imploded data.
    /// </summary>
    /// <remarks>
    /// Mirrors upstream <c>decompressPkWare</c>: an invalid header throws, but running out of input before the
    /// end marker is not an error, the bytes decoded so far are returned (the decoder's flush error is ignored upstream).
    /// </remarks>
    /// <exception cref="InvalidDataException">Unsupported compression type or dictionary size.</exception>
    public static byte[] Decompress(ReadOnlySpan<byte> input) => new Decoder(input).Run();

    private ref struct Decoder
    {
        private readonly ReadOnlySpan<byte> _in;
        private int _inPos;
        private State _state;
        private State _singleLiteralState;
        private int _dictionarySizeBits;
        private int _dictionarySizeMask;
        private int _bitBuffer;
        private int _extraBits;
        private bool _awaitingData;

        // Decoded output, prefixed with a 0x1000 byte zeroed window (upstream starts writing at workPos 0x1000).
        private byte[] _out;
        private int _outPos;

        // stateStore
        private int _decoded;
        private int _lengthCode;
        private int _asciiResult;
        private int _repetitionLength;
        private int _distPosCode;
        private int _distance;

        public Decoder(ReadOnlySpan<byte> input)
        {
            _in = input;
            _state = State.Header;
            _out = [];
        }

        public byte[] Run()
        {
            ReadHeader();
            if (IsDecoding) Decode();
            // _flush: whatever was decoded is emitted, "Not enough data" / "Unexpected end of input" are swallowed upstream
            if (_state == State.Header || _out.Length == 0) return [];
            return _out.AsSpan(0x1000, _outPos - 0x1000).ToArray();
        }

        private readonly bool IsDecoding => _state >= State.Decode && _state < State.Error;

        private void ReadHeader()
        {
            if (_in.Length < 3) return;

            var compressionType = _in[0];
            _dictionarySizeBits = _in[1];
            _bitBuffer = _in[2];
            _extraBits = 0;
            _dictionarySizeMask = 0xFFFF >> (0x10 - _dictionarySizeBits);

            if (compressionType != CtBinary && compressionType != CtAscii)
            {
                _state = State.Error;
                throw new InvalidDataException("Unsupported compression type: " + compressionType);
            }
            if (_dictionarySizeBits < 4 || _dictionarySizeBits > 6)
            {
                _state = State.Error;
                throw new InvalidDataException("Unsupported dictionary size: " + _dictionarySizeBits);
            }

            _state = State.Decode;
            _inPos = 3;
            _singleLiteralState = compressionType == CtBinary ? State.BinaryLiteral : State.AsciiLiteral;
            _out = new byte[0x2203];
            _outPos = 0x1000;
        }

        private void Decode()
        {
            while (_state < State.Terminated && !_awaitingData) Next();
        }

        private void Next()
        {
            switch (_state)
            {
                case State.Decode: DecodeInitial(); break;
                case State.RepeatedLiteral: DecodeRepeatedLiteral(); break;
                case State.RepeatedExtraLength: DecodeRepeatedExtraLength(); break;
                case State.BinaryLiteral: DecodeBinaryLiteral(); break;
                case State.AsciiLiteral: DecodeAsciiLiteral(); break;
                case State.AsciiInterpretResult: InterpretAsciiResult(); break;
                case State.InterpretRepeatedLiteral: InterpretRepeatedLiteral(); break;
                case State.GetDistanceBits: GetDistanceBits(); break;
                case State.WriteRepeatedLiteral: WriteRepeatedLiteral(); break;
                case State.WriteSingleLiteral: WriteSingleLiteral(); break;
                default: throw new InvalidOperationException("Invalid decoder state " + _state);
            }
        }

        private void LiteralDone(int lit)
        {
            _state = lit >= 0x100 ? State.InterpretRepeatedLiteral : State.WriteSingleLiteral;
            _decoded = lit;
        }

        private void DecodeInitial()
        {
            var newState = (_bitBuffer & 1) != 0 ? State.RepeatedLiteral : _singleLiteralState;
            if (ReadBits(1)) _state = newState;
        }

        private void DecodeRepeatedLiteral()
        {
            // The next 8 bits hold the index to the length code table
            var lengthCode = Tables.LengthCodes[_bitBuffer & 0xFF];
            if (!ReadBits(Tables.LenBits[lengthCode])) return;

            // Check if there are some extra bits for this length code
            if (Tables.ExLenBits[lengthCode] != 0)
            {
                _state = State.RepeatedExtraLength;
                _lengthCode = lengthCode;
            }
            else
            {
                LiteralDone(lengthCode + 0x100);
            }
        }

        private void DecodeRepeatedExtraLength()
        {
            var extraLengthBits = Tables.ExLenBits[_lengthCode];
            var extraLength = _bitBuffer & ((1 << extraLengthBits) - 1);
            if (!ReadBits(extraLengthBits))
            {
                if (_lengthCode + extraLength != EndMarker) return;
                _awaitingData = false;
                _state = State.Terminated;
                return;
            }

            var code = Tables.LenBase[_lengthCode] + extraLength;
            LiteralDone(code + 0x100);
        }

        private void DecodeBinaryLiteral()
        {
            var result = _bitBuffer;
            if (ReadBits(8)) LiteralDone(result & 0xFF);
        }

        private void DecodeAsciiLiteral()
        {
            int numBits;
            int mask;
            int[] table;

            if ((_bitBuffer & 0xFF) != 0)
            {
                var result = Tables.Ascii2C34[_bitBuffer & 0xFF];
                if (result == 0xFF)
                {
                    var b3F = _bitBuffer & 0x3F;
                    numBits = b3F != 0 ? 4 : 6;
                    mask = b3F != 0 ? 0xFF : 0x7F;
                    table = b3F != 0 ? Tables.Ascii2D34 : Tables.Ascii2E34;
                }
                else
                {
                    if (ReadBits(Tables.ChBitsAsc[result])) LiteralDone(result);
                    return;
                }
            }
            else
            {
                numBits = 8;
                mask = 0xFF;
                table = Tables.Ascii2EB4;
            }

            if (!ReadBits(numBits)) return;

            _state = State.AsciiInterpretResult;
            _asciiResult = table[_bitBuffer & mask];
        }

        private void InterpretAsciiResult()
        {
            if (!ReadBits(Tables.ChBitsAsc[_asciiResult])) return;
            LiteralDone(_asciiResult);
        }

        private void InterpretRepeatedLiteral()
        {
            // literal of 0x100 means repeating sequence of 0x2 bytes, 0x101 of 0x3 bytes ... 0x304 of 0x206 bytes
            var repetitionLength = _decoded - 0xFE;
            // decode the distance
            var distPosCode = Tables.DistPosCodes[_bitBuffer & 0xFF];
            if (!ReadBits(Tables.DistBits[distPosCode])) return;

            _state = State.GetDistanceBits;
            _repetitionLength = repetitionLength;
            _distPosCode = distPosCode;
        }

        private void GetDistanceBits()
        {
            int distance;
            int bits;
            if (_repetitionLength == 2)
            {
                // Repetition of only 2 bytes, take 2 bits from the stream to get the distance
                distance = (_distPosCode << 2) | (_bitBuffer & 0x03);
                bits = 2;
            }
            else
            {
                // Otherwise take dictionarySizeBits bits to get the distance
                distance = (_distPosCode << _dictionarySizeBits) | (_bitBuffer & _dictionarySizeMask);
                bits = _dictionarySizeBits;
            }

            if (ReadBits(bits))
            {
                _state = State.WriteRepeatedLiteral;
                _distance = distance + 1;
            }
        }

        private void WriteRepeatedLiteral()
        {
            var src = _outPos - _distance;
            var srcEnd = src + _repetitionLength;
            EnsureCapacity(_repetitionLength);
            for (; src < srcEnd; _outPos++, src++) _out[_outPos] = _out[src];
            _state = State.Decode;
        }

        private void WriteSingleLiteral()
        {
            EnsureCapacity(1);
            _out[_outPos] = (byte)_decoded;
            _outPos++;
            _state = State.Decode;
        }

        private void EnsureCapacity(int extra)
        {
            if (_outPos + extra <= _out.Length) return;
            Array.Resize(ref _out, Math.Max(_out.Length * 2, _outPos + extra));
        }

        /// <summary>
        /// Reads a number of new bits into the bit buffer, discarding old bits in the process (numBits &lt;= 8).
        /// </summary>
        private bool ReadBits(int numBits)
        {
            if (numBits <= _extraBits)
            {
                // we already have enough bits in the bit buffer, just shift
                _extraBits -= numBits;
                _bitBuffer >>= numBits;
                return true;
            }

            if (_inPos == _in.Length)
            {
                // Not enough data, don't modify the bitBuffer
                _awaitingData = true;
                return false;
            }

            // Place the new byte in the second byte of the bitBuffer
            _bitBuffer |= _in[_inPos] << (8 + _extraBits);
            _inPos++;
            // Remove the used bits
            _bitBuffer >>= numBits;
            _extraBits += 8 - numBits;
            return true;
        }
    }

    private static class Tables
    {
        public static readonly int[] DistBits =
        [
            0x02, 0x04, 0x04, 0x05, 0x05, 0x05, 0x05, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06,
            0x06, 0x06, 0x06, 0x06, 0x06, 0x06, 0x07, 0x07, 0x07, 0x07, 0x07, 0x07, 0x07, 0x07, 0x07, 0x07,
            0x07, 0x07, 0x07, 0x07, 0x07, 0x07, 0x07, 0x07, 0x07, 0x07, 0x07, 0x07, 0x07, 0x07, 0x07, 0x07,
            0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08,
        ];

        public static readonly int[] DistCode =
        [
            0x03, 0x0D, 0x05, 0x19, 0x09, 0x11, 0x01, 0x3E, 0x1E, 0x2E, 0x0E, 0x36, 0x16, 0x26, 0x06, 0x3A,
            0x1A, 0x2A, 0x0A, 0x32, 0x12, 0x22, 0x42, 0x02, 0x7C, 0x3C, 0x5C, 0x1C, 0x6C, 0x2C, 0x4C, 0x0C,
            0x74, 0x34, 0x54, 0x14, 0x64, 0x24, 0x44, 0x04, 0x78, 0x38, 0x58, 0x18, 0x68, 0x28, 0x48, 0x08,
            0xF0, 0x70, 0xB0, 0x30, 0xD0, 0x50, 0x90, 0x10, 0xE0, 0x60, 0xA0, 0x20, 0xC0, 0x40, 0x80, 0x00,
        ];

        public static readonly int[] ExLenBits =
        [
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        ];

        public static readonly int[] LenBase =
        [
            0x0000, 0x0001, 0x0002, 0x0003, 0x0004, 0x0005, 0x0006, 0x0007,
            0x0008, 0x000A, 0x000E, 0x0016, 0x0026, 0x0046, 0x0086, 0x0106,
        ];

        public static readonly int[] LenBits =
        [
            0x03, 0x02, 0x03, 0x03, 0x04, 0x04, 0x04, 0x05, 0x05, 0x05, 0x05, 0x06, 0x06, 0x06, 0x07, 0x07,
        ];

        public static readonly int[] LenCode =
        [
            0x05, 0x03, 0x01, 0x06, 0x0A, 0x02, 0x0C, 0x14, 0x04, 0x18, 0x08, 0x30, 0x10, 0x20, 0x40, 0x00,
        ];

        /// <summary>Note: upstream mutates this table while generating the ascii tables (see <see cref="GenAsciiTables"/>).</summary>
        public static readonly int[] ChBitsAsc =
        [
            0x0B, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x08, 0x07, 0x0C, 0x0C, 0x07, 0x0C, 0x0C,
            0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0D, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C,
            0x04, 0x0A, 0x08, 0x0C, 0x0A, 0x0C, 0x0A, 0x08, 0x07, 0x07, 0x08, 0x09, 0x07, 0x06, 0x07, 0x08,
            0x07, 0x06, 0x07, 0x07, 0x07, 0x07, 0x08, 0x07, 0x07, 0x08, 0x08, 0x0C, 0x0B, 0x07, 0x09, 0x0B,
            0x0C, 0x06, 0x07, 0x06, 0x06, 0x05, 0x07, 0x08, 0x08, 0x06, 0x0B, 0x09, 0x06, 0x07, 0x06, 0x06,
            0x07, 0x0B, 0x06, 0x06, 0x06, 0x07, 0x09, 0x08, 0x09, 0x09, 0x0B, 0x08, 0x0B, 0x09, 0x0C, 0x08,
            0x0C, 0x05, 0x06, 0x06, 0x06, 0x05, 0x06, 0x06, 0x06, 0x05, 0x0B, 0x07, 0x05, 0x06, 0x05, 0x05,
            0x06, 0x0A, 0x05, 0x05, 0x05, 0x05, 0x08, 0x07, 0x08, 0x08, 0x0A, 0x0B, 0x0B, 0x0C, 0x0C, 0x0C,
            0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D,
            0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D,
            0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D,
            0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C,
            0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C,
            0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C,
            0x0D, 0x0C, 0x0D, 0x0D, 0x0D, 0x0C, 0x0D, 0x0D, 0x0D, 0x0C, 0x0D, 0x0D, 0x0D, 0x0D, 0x0C, 0x0D,
            0x0D, 0x0D, 0x0C, 0x0C, 0x0C, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D,
        ];

        public static readonly int[] ChCodeAsc =
        [
            0x0490, 0x0FE0, 0x07E0, 0x0BE0, 0x03E0, 0x0DE0, 0x05E0, 0x09E0,
            0x01E0, 0x00B8, 0x0062, 0x0EE0, 0x06E0, 0x0022, 0x0AE0, 0x02E0,
            0x0CE0, 0x04E0, 0x08E0, 0x00E0, 0x0F60, 0x0760, 0x0B60, 0x0360,
            0x0D60, 0x0560, 0x1240, 0x0960, 0x0160, 0x0E60, 0x0660, 0x0A60,
            0x000F, 0x0250, 0x0038, 0x0260, 0x0050, 0x0C60, 0x0390, 0x00D8,
            0x0042, 0x0002, 0x0058, 0x01B0, 0x007C, 0x0029, 0x003C, 0x0098,
            0x005C, 0x0009, 0x001C, 0x006C, 0x002C, 0x004C, 0x0018, 0x000C,
            0x0074, 0x00E8, 0x0068, 0x0460, 0x0090, 0x0034, 0x00B0, 0x0710,
            0x0860, 0x0031, 0x0054, 0x0011, 0x0021, 0x0017, 0x0014, 0x00A8,
            0x0028, 0x0001, 0x0310, 0x0130, 0x003E, 0x0064, 0x001E, 0x002E,
            0x0024, 0x0510, 0x000E, 0x0036, 0x0016, 0x0044, 0x0030, 0x00C8,
            0x01D0, 0x00D0, 0x0110, 0x0048, 0x0610, 0x0150, 0x0060, 0x0088,
            0x0FA0, 0x0007, 0x0026, 0x0006, 0x003A, 0x001B, 0x001A, 0x002A,
            0x000A, 0x000B, 0x0210, 0x0004, 0x0013, 0x0032, 0x0003, 0x001D,
            0x0012, 0x0190, 0x000D, 0x0015, 0x0005, 0x0019, 0x0008, 0x0078,
            0x00F0, 0x0070, 0x0290, 0x0410, 0x0010, 0x07A0, 0x0BA0, 0x03A0,
            0x0240, 0x1C40, 0x0C40, 0x1440, 0x0440, 0x1840, 0x0840, 0x1040,
            0x0040, 0x1F80, 0x0F80, 0x1780, 0x0780, 0x1B80, 0x0B80, 0x1380,
            0x0380, 0x1D80, 0x0D80, 0x1580, 0x0580, 0x1980, 0x0980, 0x1180,
            0x0180, 0x1E80, 0x0E80, 0x1680, 0x0680, 0x1A80, 0x0A80, 0x1280,
            0x0280, 0x1C80, 0x0C80, 0x1480, 0x0480, 0x1880, 0x0880, 0x1080,
            0x0080, 0x1F00, 0x0F00, 0x1700, 0x0700, 0x1B00, 0x0B00, 0x1300,
            0x0DA0, 0x05A0, 0x09A0, 0x01A0, 0x0EA0, 0x06A0, 0x0AA0, 0x02A0,
            0x0CA0, 0x04A0, 0x08A0, 0x00A0, 0x0F20, 0x0720, 0x0B20, 0x0320,
            0x0D20, 0x0520, 0x0920, 0x0120, 0x0E20, 0x0620, 0x0A20, 0x0220,
            0x0C20, 0x0420, 0x0820, 0x0020, 0x0FC0, 0x07C0, 0x0BC0, 0x03C0,
            0x0DC0, 0x05C0, 0x09C0, 0x01C0, 0x0EC0, 0x06C0, 0x0AC0, 0x02C0,
            0x0CC0, 0x04C0, 0x08C0, 0x00C0, 0x0F40, 0x0740, 0x0B40, 0x0340,
            0x0300, 0x0D40, 0x1D00, 0x0D00, 0x1500, 0x0540, 0x0500, 0x1900,
            0x0900, 0x0940, 0x1100, 0x0100, 0x1E00, 0x0E00, 0x0140, 0x1600,
            0x0600, 0x1A00, 0x0E40, 0x0640, 0x0A40, 0x0A00, 0x1200, 0x0200,
            0x1C00, 0x0C00, 0x1400, 0x0400, 0x1800, 0x0800, 0x1000, 0x0000,
        ];

        public static readonly int[] LengthCodes = GenDecodeTables(LenCode, LenBits);
        public static readonly int[] DistPosCodes = GenDecodeTables(DistCode, DistBits);

        public static readonly int[] Ascii2C34 = new int[0x100];
        public static readonly int[] Ascii2D34 = new int[0x100];
        public static readonly int[] Ascii2E34 = new int[0x80];
        public static readonly int[] Ascii2EB4 = new int[0x100];

        static Tables() => GenAsciiTables();

        private static int[] GenDecodeTables(int[] startIndexes, int[] lengthBits)
        {
            var result = new int[0x100];
            for (var i = 0; i < startIndexes.Length; i++)
            {
                var length = 1 << lengthBits[i];
                for (var j = startIndexes[i]; j < 0x100; j += length) result[j] = i;
            }
            return result;
        }

        private static void GenAsciiTables()
        {
            var codeIndex = 0xFF;
            for (var count = 0x00FF; codeIndex >= 0; codeIndex--, count--)
            {
                var bitsIndex = count;
                var bitsValue = ChBitsAsc[bitsIndex];
                int add;
                int acc;
                if (bitsValue <= 8)
                {
                    add = 1 << bitsValue;
                    acc = ChCodeAsc[codeIndex];
                    do
                    {
                        Ascii2C34[acc] = count;
                        acc += add;
                    } while (acc < 0x100);
                }
                else if ((acc = ChCodeAsc[codeIndex] & 0xFF) != 0)
                {
                    Ascii2C34[acc] = 0xFF;

                    if ((ChCodeAsc[codeIndex] & 0x3F) != 0)
                    {
                        bitsValue -= 4;
                        ChBitsAsc[bitsIndex] = bitsValue;

                        add = 1 << bitsValue;
                        acc = ChCodeAsc[codeIndex] >> 4;
                        do
                        {
                            Ascii2D34[acc] = count;
                            acc += add;
                        } while (acc < 0x100);
                    }
                    else
                    {
                        bitsValue -= 6;
                        ChBitsAsc[bitsIndex] = bitsValue;

                        add = 1 << bitsValue;
                        acc = ChCodeAsc[codeIndex] >> 6;
                        do
                        {
                            Ascii2E34[acc] = count;
                            acc += add;
                        } while (acc < 0x80);
                    }
                }
                else
                {
                    bitsValue -= 8;
                    ChBitsAsc[bitsIndex] = bitsValue;

                    add = 1 << bitsValue;
                    acc = ChCodeAsc[codeIndex] >> 8;
                    do
                    {
                        Ascii2EB4[acc] = count;
                        acc += add;
                    } while (acc < 0x100);
                }
            }
        }
    }
}
