using PlcComLib.DataTypes;

namespace PlcComLib.Core.PlcTypes;

/// <summary>
/// Fixed-length raw byte buffer with a compile-time size.
/// Assign and read as <see cref="byte"/>[] via implicit operators.
/// <code>public S7Raw&lt;L16&gt; Header { get; set; }</code>
/// </summary>
/// <typeparam name="TLen">
/// One of the pre-defined length structs (<see cref="L16"/>, <see cref="L32"/>, …) or a
/// custom struct implementing <see cref="IS7Length"/>.
/// </typeparam>
public readonly struct S7Raw<TLen>(byte[]? value) : IS7FramingType
    where TLen : struct, IS7Length
{
    private readonly byte[]? _v = value;

    /// <summary>The byte array. Never null — returns an empty array for default instances.</summary>
    public byte[] Value => _v ?? [];

    /// <summary>Number of bytes on the wire as declared by <typeparamref name="TLen"/>.</summary>
    public static int ByteCount => TLen.Value;

    public static S7DataType DataType => S7DataType.Raw;
    public static int WireSize => TLen.Value;

    public static implicit operator S7Raw<TLen>(byte[] v) => new(v);
    public static implicit operator byte[](S7Raw<TLen> r) => r._v ?? [];

    public override string ToString() => $"byte[{_v?.Length ?? 0}]";
}
