using System.Buffers.Binary;
using System.Globalization;
using ProtobufGenerated;

namespace MacroRecorder.Waiting;

public readonly record struct ScalarValue(MemoryScalarType Type, decimal Integer, double Floating)
{
    public bool IsFloating => Type is MemoryScalarType.Float32 or MemoryScalarType.Float64;
    public static int Width(MemoryScalarType type) => type switch
    {
        MemoryScalarType.Uint8 or MemoryScalarType.Int8 => 1,
        MemoryScalarType.Uint16 or MemoryScalarType.Int16 => 2,
        MemoryScalarType.Uint32 or MemoryScalarType.Int32 or MemoryScalarType.Float32 => 4,
        MemoryScalarType.Uint64 or MemoryScalarType.Int64 or MemoryScalarType.Float64 => 8,
        _ => throw new ArgumentException("Unsupported scalar type.")
    };

    public static ScalarValue Parse(MemoryScalarType type, string value)
    {
        if (value.Length is 0 or > 128 || value.Any(char.IsWhiteSpace)) throw new ArgumentException("Enter one invariant decimal scalar.");
        try
        {
            var culture = CultureInfo.InvariantCulture;
            const NumberStyles style = NumberStyles.AllowLeadingSign;
            return type switch
            {
                MemoryScalarType.Uint8 => new(type, byte.Parse(value, style, culture), 0),
                MemoryScalarType.Int8 => new(type, sbyte.Parse(value, style, culture), 0),
                MemoryScalarType.Uint16 => new(type, ushort.Parse(value, style, culture), 0),
                MemoryScalarType.Int16 => new(type, short.Parse(value, style, culture), 0),
                MemoryScalarType.Uint32 => new(type, uint.Parse(value, style, culture), 0),
                MemoryScalarType.Int32 => new(type, int.Parse(value, style, culture), 0),
                MemoryScalarType.Uint64 => new(type, ulong.Parse(value, style, culture), 0),
                MemoryScalarType.Int64 => new(type, long.Parse(value, style, culture), 0),
                MemoryScalarType.Float32 => Finite(type, float.Parse(value, NumberStyles.Float, culture)),
                MemoryScalarType.Float64 => Finite(type, double.Parse(value, NumberStyles.Float, culture)),
                _ => throw new ArgumentException("Unsupported scalar type.")
            };
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        { throw new ArgumentException("Expected value is outside the selected scalar type or is not an invariant decimal.", error); }
    }

    private static ScalarValue Finite(MemoryScalarType type, double value) => double.IsFinite(value)
        ? new(type, 0, value) : throw new ArgumentException("Floating-point values must be finite.");

    public static ScalarValue Decode(MemoryScalarType type, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Width(type)) throw new ArgumentException("A complete scalar read is required.");
        return type switch
        {
            MemoryScalarType.Uint8 => new(type, bytes[0], 0),
            MemoryScalarType.Int8 => new(type, (sbyte)bytes[0], 0),
            MemoryScalarType.Uint16 => new(type, BinaryPrimitives.ReadUInt16LittleEndian(bytes), 0),
            MemoryScalarType.Int16 => new(type, BinaryPrimitives.ReadInt16LittleEndian(bytes), 0),
            MemoryScalarType.Uint32 => new(type, BinaryPrimitives.ReadUInt32LittleEndian(bytes), 0),
            MemoryScalarType.Int32 => new(type, BinaryPrimitives.ReadInt32LittleEndian(bytes), 0),
            MemoryScalarType.Uint64 => new(type, BinaryPrimitives.ReadUInt64LittleEndian(bytes), 0),
            MemoryScalarType.Int64 => new(type, BinaryPrimitives.ReadInt64LittleEndian(bytes), 0),
            MemoryScalarType.Float32 => Finite(type, BinaryPrimitives.ReadSingleLittleEndian(bytes)),
            MemoryScalarType.Float64 => Finite(type, BinaryPrimitives.ReadDoubleLittleEndian(bytes)),
            _ => throw new ArgumentException("Unsupported scalar type.")
        };
    }

    public bool Matches(ScalarValue expected, NumericComparison comparison, double tolerance)
    {
        if (Type != expected.Type) throw new ArgumentException("Scalar types differ.");
        var order = IsFloating ? Floating.CompareTo(expected.Floating) : Integer.CompareTo(expected.Integer);
        var equal = IsFloating ? Math.Abs(Floating - expected.Floating) <= tolerance : order == 0;
        return comparison switch
        {
            NumericComparison.NumericEquals => equal, NumericComparison.NumericNotEquals => !equal,
            NumericComparison.Less => order < 0, NumericComparison.LessOrEqual => order <= 0,
            NumericComparison.Greater => order > 0, NumericComparison.GreaterOrEqual => order >= 0,
            _ => throw new ArgumentException("Unsupported scalar comparison.")
        };
    }
}
