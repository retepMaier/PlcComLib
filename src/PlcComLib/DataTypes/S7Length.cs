namespace PlcComLib.DataTypes;

/// <summary>
/// Encodes a compile-time length constant for use as a type argument in
/// <see cref="S7String{TLen}"/>, <see cref="S7WString{TLen}"/>,
/// <see cref="S7Raw{TLen}"/>, and <see cref="S7CharArray{TLen}"/>.
/// <para>
/// Common lengths are pre-defined as <c>L1</c>…<c>L254</c>.
/// For a custom length, implement the interface in a small struct:
/// <code>public struct L42 : IS7Length { public static int Value =&gt; 42; }</code>
/// </para>
/// </summary>
public interface IS7Length
{
    static abstract int Value { get; }
}

// ── Common pre-defined lengths ────────────────────────────────────────────────

public struct L1   : IS7Length { public static int Value =>   1; }
public struct L2   : IS7Length { public static int Value =>   2; }
public struct L4   : IS7Length { public static int Value =>   4; }
public struct L8   : IS7Length { public static int Value =>   8; }
public struct L10  : IS7Length { public static int Value =>  10; }
public struct L12  : IS7Length { public static int Value =>  12; }
public struct L16  : IS7Length { public static int Value =>  16; }
public struct L20  : IS7Length { public static int Value =>  20; }
public struct L24  : IS7Length { public static int Value =>  24; }
public struct L32  : IS7Length { public static int Value =>  32; }
public struct L40  : IS7Length { public static int Value =>  40; }
public struct L48  : IS7Length { public static int Value =>  48; }
public struct L50  : IS7Length { public static int Value =>  50; }
public struct L64  : IS7Length { public static int Value =>  64; }
public struct L80  : IS7Length { public static int Value =>  80; }
public struct L100 : IS7Length { public static int Value => 100; }
public struct L128 : IS7Length { public static int Value => 128; }
public struct L160 : IS7Length { public static int Value => 160; }
public struct L200 : IS7Length { public static int Value => 200; }
public struct L254 : IS7Length { public static int Value => 254; }
