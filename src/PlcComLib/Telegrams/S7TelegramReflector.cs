using System.Buffers;
using System.Buffers.Binary;
using System.Linq.Expressions;
using System.Reflection;
using PlcComLib.DataTypes;

namespace PlcComLib.Telegrams;

/// <summary>
/// Per-type serialisation engine for <see cref="S7TelegramBase{TSelf}"/>.
/// Builds a field plan once (on first use, in the static constructor) using
/// Expression-tree compilation, then dispatches through pre-compiled
/// <c>Action&lt;T, byte[], int, bool&gt;</c> delegates — no runtime reflection,
/// no boxing of the telegram instance.
/// </summary>
internal static class S7TelegramReflector<T> where T : class, new()
{
    internal static readonly TelegramDefinition Definition;
    internal static readonly int WireSize;

    private static readonly FieldPlan[] _plan;

    // ── Static constructor — runs exactly once per concrete T ─────────────────
    static S7TelegramReflector()
    {
        _plan      = BuildPlan(typeof(T));
        WireSize   = _plan.Sum(static p => p.WireSize);
        Definition = BuildDefinition();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    internal static byte[] Serialize(T telegram, DataTypes.ByteOrder byteOrder)
    {
        bool le  = byteOrder == DataTypes.ByteOrder.LittleEndian;
        var  buf = new byte[WireSize];
        foreach (var p in _plan)
            p.Serialize(telegram, buf, p.Offset, le);
        return buf;
    }

    internal static T Deserialize(ReadOnlySpan<byte> data, DataTypes.ByteOrder byteOrder)
    {
        if (data.Length < WireSize)
            throw new ArgumentException(
                $"Buffer too short for {typeof(T).Name}: expected {WireSize} bytes, got {data.Length}.");

        bool le  = byteOrder == DataTypes.ByteOrder.LittleEndian;
        var  obj = new T();

        // Rent a byte[] so the field delegates can use spans without unsafe code.
        byte[] rented = ArrayPool<byte>.Shared.Rent(data.Length);
        try
        {
            data.CopyTo(rented);
            foreach (var p in _plan)
                p.Deserialize(obj, rented, p.Offset, le);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }

        return obj;
    }

    // ── Plan building — runs once ─────────────────────────────────────────────

    private static FieldPlan[] BuildPlan(Type type)
    {
        // Collect all public read/write properties whose type implements IS7FramingType,
        // in source-declaration order. MetadataToken is stable within a single type and
        // reflects the order properties appear in the source file after compilation by
        // the C# compiler. For flat (non-inherited) telegram classes this gives
        // deterministic, declaration-order field layout.
        var props = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static p => p.CanRead && p.CanWrite &&
                               typeof(IS7FramingType).IsAssignableFrom(p.PropertyType))
            .OrderBy(static p => p.MetadataToken)
            .ToArray();

        var plans  = new List<FieldPlan>(props.Length);
        int offset = 0;

        foreach (var prop in props)
        {
            var propType   = prop.PropertyType;
            var dataType   = (S7DataType)propType.GetProperty("DataType",   BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
            var wireSize   = (int)       propType.GetProperty("WireSize",   BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
            var maxLenProp =             propType.GetProperty("MaxLength",  BindingFlags.Static | BindingFlags.Public);
            int maxLength  = maxLenProp is not null ? (int)maxLenProp.GetValue(null)! : 0;

            var (ser, deser) = CreateDelegates(prop, dataType, wireSize, maxLength);

            plans.Add(new FieldPlan(
                Name:       prop.Name,
                DataType:   dataType,
                WireSize:   wireSize,
                Offset:     offset,
                MaxLength:  maxLength,
                Serialize:  ser,
                Deserialize: deser));

            offset += wireSize;
        }

        return [.. plans];
    }

    private static TelegramDefinition BuildDefinition() => new()
    {
        Id                 = typeof(T).Name,
        ConfiguredWireSize = WireSize,
        Fields             = _plan.Select(static p => new TelegramField
        {
            Name            = p.Name,
            DataType        = p.DataType,
            MaxStringLength = p.DataType is S7DataType.S7String or S7DataType.S7WString
                                  ? (byte)Math.Min(p.MaxLength, 254)
                                  : (byte)0,
            RawByteCount    = p.DataType is S7DataType.Raw or S7DataType.CharArray
                                  ? p.WireSize
                                  : 0,
        }).ToList(),
    };

    // ── Delegate factory — one delegate pair per field ────────────────────────

    private static (Action<T, byte[], int, bool> Ser, Action<T, byte[], int, bool> Deser)
        CreateDelegates(PropertyInfo prop, S7DataType dataType, int wireSize, int maxLength)
    {
        Action<T, byte[], int, bool> ser;
        Action<T, byte[], int, bool> deser;

        switch (dataType)
        {
            // ── 1-byte ───────────────────────────────────────────────────────
            case S7DataType.Bool:
            {
                var get = CompileGetter<bool>(prop);
                var set = CompileSetter<bool>(prop);
                ser   = (inst, buf, off, _)  => buf[off] = get(inst) ? (byte)1 : (byte)0;
                deser = (inst, buf, off, _)  => set(inst, buf[off] != 0);
                break;
            }
            case S7DataType.Byte:
            case S7DataType.USInt:
            {
                var get = CompileGetter<byte>(prop);
                var set = CompileSetter<byte>(prop);
                ser   = (inst, buf, off, _) => buf[off] = get(inst);
                deser = (inst, buf, off, _) => set(inst, buf[off]);
                break;
            }
            case S7DataType.SInt:
            {
                var get = CompileGetter<sbyte>(prop);
                var set = CompileSetter<sbyte>(prop);
                ser   = (inst, buf, off, _) => buf[off] = unchecked((byte)get(inst));
                deser = (inst, buf, off, _) => set(inst, (sbyte)buf[off]);
                break;
            }
            case S7DataType.Char:
            {
                var get = CompileGetter<char>(prop);
                var set = CompileSetter<char>(prop);
                ser   = (inst, buf, off, _) => buf[off] = (byte)get(inst);
                deser = (inst, buf, off, _) => set(inst, (char)buf[off]);
                break;
            }

            // ── 2-byte ───────────────────────────────────────────────────────
            case S7DataType.Word:
            case S7DataType.UInt:
            {
                var get = CompileGetter<ushort>(prop);
                var set = CompileSetter<ushort>(prop);
                ser = (inst, buf, off, le) =>
                {
                    if (le) BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(off, 2), get(inst));
                    else    BinaryPrimitives.WriteUInt16BigEndian   (buf.AsSpan(off, 2), get(inst));
                };
                deser = (inst, buf, off, le) => set(inst,
                    le ? BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(off, 2))
                       : BinaryPrimitives.ReadUInt16BigEndian   (buf.AsSpan(off, 2)));
                break;
            }
            case S7DataType.Int:
            case S7DataType.Date:
            {
                var get = CompileGetter<short>(prop);
                var set = CompileSetter<short>(prop);
                ser = (inst, buf, off, le) =>
                {
                    if (le) BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(off, 2), get(inst));
                    else    BinaryPrimitives.WriteInt16BigEndian   (buf.AsSpan(off, 2), get(inst));
                };
                deser = (inst, buf, off, le) => set(inst,
                    le ? BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(off, 2))
                       : BinaryPrimitives.ReadInt16BigEndian   (buf.AsSpan(off, 2)));
                break;
            }
            case S7DataType.WChar:
            {
                var get = CompileGetter<char>(prop);
                var set = CompileSetter<char>(prop);
                ser = (inst, buf, off, le) =>
                {
                    ushort v = (ushort)get(inst);
                    if (le) BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(off, 2), v);
                    else    BinaryPrimitives.WriteUInt16BigEndian   (buf.AsSpan(off, 2), v);
                };
                deser = (inst, buf, off, le) => set(inst,
                    (char)(le ? BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(off, 2))
                               : BinaryPrimitives.ReadUInt16BigEndian   (buf.AsSpan(off, 2))));
                break;
            }

            // ── 4-byte ───────────────────────────────────────────────────────
            case S7DataType.DWord:
            case S7DataType.UDInt:
            case S7DataType.TimeOfDay:
            {
                var get = CompileGetter<uint>(prop);
                var set = CompileSetter<uint>(prop);
                ser = (inst, buf, off, le) =>
                {
                    if (le) BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(off, 4), get(inst));
                    else    BinaryPrimitives.WriteUInt32BigEndian   (buf.AsSpan(off, 4), get(inst));
                };
                deser = (inst, buf, off, le) => set(inst,
                    le ? BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(off, 4))
                       : BinaryPrimitives.ReadUInt32BigEndian   (buf.AsSpan(off, 4)));
                break;
            }
            case S7DataType.DInt:
            case S7DataType.Time:
            {
                var get = CompileGetter<int>(prop);
                var set = CompileSetter<int>(prop);
                ser = (inst, buf, off, le) =>
                {
                    if (le) BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(off, 4), get(inst));
                    else    BinaryPrimitives.WriteInt32BigEndian   (buf.AsSpan(off, 4), get(inst));
                };
                deser = (inst, buf, off, le) => set(inst,
                    le ? BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(off, 4))
                       : BinaryPrimitives.ReadInt32BigEndian   (buf.AsSpan(off, 4)));
                break;
            }
            case S7DataType.Real:
            {
                var get = CompileGetter<float>(prop);
                var set = CompileSetter<float>(prop);
                ser = (inst, buf, off, le) =>
                {
                    float v = get(inst);
                    if (le) BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(off, 4), BitConverter.SingleToUInt32Bits(v));
                    else    ByteSwapper.WriteReal(buf.AsSpan(off), v);
                };
                deser = (inst, buf, off, le) => set(inst,
                    le ? BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(off, 4)))
                       : ByteSwapper.ReadReal(buf.AsSpan(off, 4)));
                break;
            }

            // ── 8-byte ───────────────────────────────────────────────────────
            case S7DataType.LWord:
            case S7DataType.ULInt:
            {
                var get = CompileGetter<ulong>(prop);
                var set = CompileSetter<ulong>(prop);
                ser = (inst, buf, off, le) =>
                {
                    if (le) BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(off, 8), get(inst));
                    else    BinaryPrimitives.WriteUInt64BigEndian   (buf.AsSpan(off, 8), get(inst));
                };
                deser = (inst, buf, off, le) => set(inst,
                    le ? BinaryPrimitives.ReadUInt64LittleEndian(buf.AsSpan(off, 8))
                       : BinaryPrimitives.ReadUInt64BigEndian   (buf.AsSpan(off, 8)));
                break;
            }
            case S7DataType.LInt:
            {
                var get = CompileGetter<long>(prop);
                var set = CompileSetter<long>(prop);
                ser = (inst, buf, off, le) =>
                {
                    if (le) BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(off, 8), get(inst));
                    else    BinaryPrimitives.WriteInt64BigEndian   (buf.AsSpan(off, 8), get(inst));
                };
                deser = (inst, buf, off, le) => set(inst,
                    le ? BinaryPrimitives.ReadInt64LittleEndian(buf.AsSpan(off, 8))
                       : BinaryPrimitives.ReadInt64BigEndian   (buf.AsSpan(off, 8)));
                break;
            }
            case S7DataType.LReal:
            {
                var get = CompileGetter<double>(prop);
                var set = CompileSetter<double>(prop);
                ser = (inst, buf, off, le) =>
                {
                    double v = get(inst);
                    if (le) BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(off, 8), BitConverter.DoubleToUInt64Bits(v));
                    else    ByteSwapper.WriteLReal(buf.AsSpan(off), v);
                };
                deser = (inst, buf, off, le) => set(inst,
                    le ? BitConverter.UInt64BitsToDouble(BinaryPrimitives.ReadUInt64LittleEndian(buf.AsSpan(off, 8)))
                       : ByteSwapper.ReadLReal(buf.AsSpan(off, 8)));
                break;
            }
            case S7DataType.DateAndTime:
            {
                int ws = wireSize;
                var get = CompileGetter<DateTime>(prop);
                var set = CompileSetter<DateTime>(prop);
                ser = (inst, buf, off, _) =>
                {
                    var bytes = S7TypeConverter.Serialize(S7DataType.DateAndTime, get(inst));
                    bytes.CopyTo(buf, off);
                };
                deser = (inst, buf, off, _) => set(inst,
                    (DateTime)S7TypeConverter.Deserialize(S7DataType.DateAndTime, buf.AsSpan(off, ws)));
                break;
            }

            // ── Variable-length ───────────────────────────────────────────────
            case S7DataType.S7String:
            {
                int ml = maxLength;
                int ws = wireSize;
                var get = CompileGetter<string>(prop);
                var set = CompileSetter<string>(prop);
                ser   = (inst, buf, off, _) =>
                    new S7String(get(inst) ?? string.Empty, (byte)ml).WriteTo(buf.AsSpan(off, ws));
                deser = (inst, buf, off, _) =>
                    set(inst, S7String.ReadFrom(buf.AsSpan(off, ws)).Value);
                break;
            }
            case S7DataType.S7WString:
            {
                int ml = maxLength;
                int ws = wireSize;
                var get = CompileGetter<string>(prop);
                var set = CompileSetter<string>(prop);
                ser   = (inst, buf, off, _) =>
                    new S7WString(get(inst) ?? string.Empty, (ushort)ml).WriteTo(buf.AsSpan(off, ws));
                deser = (inst, buf, off, _) =>
                    set(inst, S7WString.ReadFrom(buf.AsSpan(off, ws)).Value);
                break;
            }
            case S7DataType.Raw:
            {
                int ws = wireSize;
                var get = CompileGetter<byte[]>(prop);
                var set = CompileSetter<byte[]>(prop);
                ser = (inst, buf, off, _) =>
                {
                    var v = get(inst);
                    if (v is { Length: > 0 })
                        v.AsSpan(0, Math.Min(v.Length, ws)).CopyTo(buf.AsSpan(off, ws));
                };
                deser = (inst, buf, off, _) => set(inst, buf.AsSpan(off, ws).ToArray());
                break;
            }
            case S7DataType.CharArray:
            {
                int ws = wireSize;
                var get = CompileGetter<char[]>(prop);
                var set = CompileSetter<char[]>(prop);
                ser = (inst, buf, off, _) =>
                {
                    var ca  = get(inst);
                    int len = ca?.Length ?? 0;
                    for (int i = 0; i < ws; i++)
                        buf[off + i] = i < len ? (byte)ca![i] : (byte)0;
                };
                deser = (inst, buf, off, _) =>
                {
                    var ca = new char[ws];
                    for (int i = 0; i < ws; i++) ca[i] = (char)buf[off + i];
                    set(inst, ca);
                };
                break;
            }

            default:
                throw new NotSupportedException(
                    $"S7 data type '{dataType}' is not supported in {nameof(S7TelegramReflector<T>)}.");
        }

        return (ser, deser);
    }

    // ── Expression-tree helpers — compile once, zero runtime reflection ───────

    /// <summary>
    /// Compiles a getter <c>Func&lt;T, TPrimitive&gt;</c> that reads the property and
    /// converts it to <typeparamref name="TPrimitive"/> via the implicit operator.
    /// </summary>
    private static Func<T, TPrimitive> CompileGetter<TPrimitive>(PropertyInfo prop)
    {
        var inst    = Expression.Parameter(typeof(T), "inst");
        var propExp = Expression.Property(inst, prop);
        var body    = propExp.Type == typeof(TPrimitive)
            ? (Expression)propExp
            : Expression.Convert(propExp, typeof(TPrimitive));   // calls op_Implicit
        return Expression.Lambda<Func<T, TPrimitive>>(body, inst).Compile();
    }

    /// <summary>
    /// Compiles a setter <c>Action&lt;T, TPrimitive&gt;</c> that converts the primitive
    /// back to the property type via the implicit operator and assigns the property.
    /// </summary>
    private static Action<T, TPrimitive> CompileSetter<TPrimitive>(PropertyInfo prop)
    {
        var inst    = Expression.Parameter(typeof(T), "inst");
        var val     = Expression.Parameter(typeof(TPrimitive), "val");
        var valConv = typeof(TPrimitive) == prop.PropertyType
            ? (Expression)val
            : Expression.Convert(val, prop.PropertyType);        // calls op_Implicit
        var assign  = Expression.Assign(Expression.Property(inst, prop), valConv);
        return Expression.Lambda<Action<T, TPrimitive>>(assign, inst, val).Compile();
    }

    // ── Field plan record ─────────────────────────────────────────────────────

    private sealed class FieldPlan(
        string Name,
        S7DataType DataType,
        int WireSize,
        int Offset,
        int MaxLength,
        Action<T, byte[], int, bool> Serialize,
        Action<T, byte[], int, bool> Deserialize)
    {
        public string     Name       { get; } = Name;
        public S7DataType DataType   { get; } = DataType;
        public int        WireSize   { get; } = WireSize;
        public int        Offset     { get; } = Offset;
        public int        MaxLength  { get; } = MaxLength;
        public Action<T, byte[], int, bool> Serialize   { get; } = Serialize;
        public Action<T, byte[], int, bool> Deserialize { get; } = Deserialize;
    }
}
