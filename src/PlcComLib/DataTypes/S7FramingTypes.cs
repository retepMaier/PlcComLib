namespace PlcComLib.DataTypes;

/// <summary>Framing marker for a 1-byte unsigned S7 BYTE / USInt field.</summary>
public readonly struct S7Byte   : IS7FramingType { public static S7DataType DataType => S7DataType.Byte;  }

/// <summary>Framing marker for a 1-byte signed S7 SInt field.</summary>
public readonly struct S7SInt   : IS7FramingType { public static S7DataType DataType => S7DataType.SInt;  }

/// <summary>Framing marker for a 2-byte unsigned S7 WORD / UInt field.</summary>
public readonly struct S7Word   : IS7FramingType { public static S7DataType DataType => S7DataType.Word;  }

/// <summary>Framing marker for a 2-byte signed S7 INT field.</summary>
public readonly struct S7Int    : IS7FramingType { public static S7DataType DataType => S7DataType.Int;   }

/// <summary>Framing marker for a 4-byte unsigned S7 DWORD / UDInt field.</summary>
public readonly struct S7DWord  : IS7FramingType { public static S7DataType DataType => S7DataType.DWord; }

/// <summary>Framing marker for a 4-byte signed S7 DINT field.</summary>
public readonly struct S7DInt   : IS7FramingType { public static S7DataType DataType => S7DataType.DInt;  }

/// <summary>Framing marker for an 8-byte unsigned S7 LWORD / ULInt field.</summary>
public readonly struct S7LWord  : IS7FramingType { public static S7DataType DataType => S7DataType.LWord; }

/// <summary>Framing marker for an 8-byte signed S7 LINT field.</summary>
public readonly struct S7LInt   : IS7FramingType { public static S7DataType DataType => S7DataType.LInt;  }
