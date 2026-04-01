using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PlcComLib.SourceGenerator;

[Generator]
public sealed class S7TelegramGenerator : IIncrementalGenerator
{
    private const string GenNs = "PlcComLib.SourceGenerator";
    private const string TelegramAttrFqn = "PlcComLib.SourceGenerator.S7TelegramAttribute";

    // ──────────────────────────────────────────────────────────────────────────
    // Initialise pipeline
    // ──────────────────────────────────────────────────────────────────────────

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
            ctx.AddSource("S7TelegramAttributes.g.cs",
                SourceText.From(AttributeSource, Encoding.UTF8)));

        var telegrams = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) =>
                    node is ClassDeclarationSyntax cls
                    && cls.Modifiers.Any(SyntaxKind.PartialKeyword)
                    && cls.AttributeLists.Count > 0,
                transform: static (ctx, ct) => Transform(ctx, ct))
            .Where(static t => t is not null)
            .Select(static (t, _) => t!);

        context.RegisterSourceOutput(telegrams, static (ctx, info) =>
            ctx.AddSource(
                $"{info.HintName}.S7Telegram.g.cs",
                SourceText.From(GenerateCode(info), Encoding.UTF8)));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Syntax → model
    // ──────────────────────────────────────────────────────────────────────────

    private static TelegramClassInfo? Transform(GeneratorSyntaxContext ctx,System.Threading.CancellationToken ct)
    {
        var cls = (ClassDeclarationSyntax)ctx.Node;
        if (ctx.SemanticModel.GetDeclaredSymbol(cls, ct) is not INamedTypeSymbol symbol)
            return null;

        AttributeData? telegramAttr = null;
        foreach (var a in symbol.GetAttributes())
        {
            if (a.AttributeClass?.ToDisplayString() == TelegramAttrFqn)
            {
                telegramAttr = a;
                break;
            }
        }
        if (telegramAttr is null) return null;

        var fields = new List<FieldInfo>();
        foreach (var member in cls.Members.OfType<PropertyDeclarationSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            if (ctx.SemanticModel.GetDeclaredSymbol(member, ct) is not IPropertySymbol prop)
                continue;

            var fi = BuildFieldInfo(prop.GetAttributes(), prop.Name);
            if (fi is not null) fields.Add(fi);
        }

        string? ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : symbol.ContainingNamespace.ToDisplayString();

        return new TelegramClassInfo(symbol.Name, ns, fields.ToImmutableArray());
    }

    private static FieldInfo? BuildFieldInfo(ImmutableArray<AttributeData> attrs, string name)
    {
        foreach (var attr in attrs)
        {
            var fqn = attr.AttributeClass?.ToDisplayString() ?? "";
            if (!fqn.StartsWith(GenNs + ".", System.StringComparison.Ordinal)) continue;

            var shortName = attr.AttributeClass!.Name;

            switch (shortName)
            {
                case "S7BoolAttribute":
                    return new(name, S7Kind.Bool, "global::PlcComLib.DataTypes.S7DataType.Bool", 1, 0, 0);
                case "S7ByteAttribute":
                case "S7USIntAttribute":
                    return new(name, S7Kind.Byte, "global::PlcComLib.DataTypes.S7DataType.Byte", 1, 0, 0);
                case "S7SIntAttribute":
                    return new(name, S7Kind.SInt, "global::PlcComLib.DataTypes.S7DataType.SInt", 1, 0, 0);
                case "S7CharAttribute":
                    return new(name, S7Kind.Char1, "global::PlcComLib.DataTypes.S7DataType.Char", 1, 0, 0);
                case "S7WordAttribute":
                case "S7UIntAttribute":
                    return new(name, S7Kind.Word, "global::PlcComLib.DataTypes.S7DataType.Word", 2, 0, 0);
                case "S7IntAttribute":
                    return new(name, S7Kind.Int, "global::PlcComLib.DataTypes.S7DataType.Int", 2, 0, 0);
                case "S7DateAttribute":
                    return new(name, S7Kind.Int, "global::PlcComLib.DataTypes.S7DataType.Date", 2, 0, 0);
                case "S7WCharAttribute":
                    return new(name, S7Kind.WChar, "global::PlcComLib.DataTypes.S7DataType.WChar", 2, 0, 0);
                case "S7DWordAttribute":
                case "S7UDIntAttribute":
                case "S7TimeOfDayAttribute":
                    return new(name, S7Kind.DWord, "global::PlcComLib.DataTypes.S7DataType.DWord", 4, 0, 0);
                case "S7DIntAttribute":
                case "S7TimeAttribute":
                    return new(name, S7Kind.DInt, "global::PlcComLib.DataTypes.S7DataType.DInt", 4, 0, 0);
                case "S7RealAttribute":
                    return new(name, S7Kind.Real, "global::PlcComLib.DataTypes.S7DataType.Real", 4, 0, 0);
                case "S7LWordAttribute":
                case "S7ULIntAttribute":
                    return new(name, S7Kind.LWord, "global::PlcComLib.DataTypes.S7DataType.LWord", 8, 0, 0);
                case "S7LIntAttribute":
                    return new(name, S7Kind.LInt, "global::PlcComLib.DataTypes.S7DataType.LInt", 8, 0, 0);
                case "S7LRealAttribute":
                    return new(name, S7Kind.LReal, "global::PlcComLib.DataTypes.S7DataType.LReal", 8, 0, 0);
                case "S7DateAndTimeAttribute":
                    return new(name, S7Kind.DateAndTime, "global::PlcComLib.DataTypes.S7DataType.DateAndTime", 8, 0, 0);
                case "S7StringAttribute":
                {
                    byte maxLen = 254;
                    if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is not null)
                        maxLen = (byte)System.Convert.ToUInt32(attr.ConstructorArguments[0].Value);
                    return new(name, S7Kind.S7String, "global::PlcComLib.DataTypes.S7DataType.S7String",
                        2 + maxLen, maxLen, 0);
                }
                case "S7WStringAttribute":
                {
                    ushort maxLen = 254;
                    if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is not null)
                        maxLen = (ushort)System.Convert.ToUInt32(attr.ConstructorArguments[0].Value);
                    return new(name, S7Kind.S7WString, "global::PlcComLib.DataTypes.S7DataType.S7WString",
                        4 + maxLen * 2, maxLen, 0);
                }
                case "S7RawAttribute":
                {
                    int byteCount = 0;
                    if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is not null)
                        byteCount = System.Convert.ToInt32(attr.ConstructorArguments[0].Value);
                    return new(name, S7Kind.Raw, "global::PlcComLib.DataTypes.S7DataType.Raw", byteCount, 0, byteCount);
                }
                case "S7CharArrayAttribute":
                {
                    int length = 0;
                    if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is not null)
                        length = System.Convert.ToInt32(attr.ConstructorArguments[0].Value);
                    return new(name, S7Kind.CharArray, "global::PlcComLib.DataTypes.S7DataType.CharArray", length, 0, length);
                }
            }
        }
        return null;
    }

    //private static string GetPropTypeName(ITypeSymbol type) => type.SpecialType switch
    //{
    //    SpecialType.System_Boolean => "bool",
    //    SpecialType.System_Byte    => "byte",
    //    SpecialType.System_SByte   => "sbyte",
    //    SpecialType.System_UInt16  => "ushort",
    //    SpecialType.System_Int16   => "short",
    //    SpecialType.System_UInt32  => "uint",
    //    SpecialType.System_Int32   => "int",
    //    SpecialType.System_UInt64  => "ulong",
    //    SpecialType.System_Int64   => "long",
    //    SpecialType.System_Single  => "float",
    //    SpecialType.System_Double  => "double",
    //    _                          => type.ToDisplayString(),
    //};

    // ──────────────────────────────────────────────────────────────────────────
    // Code emission
    // ──────────────────────────────────────────────────────────────────────────

    private static string GenerateCode(TelegramClassInfo info)
    {
        // Wire size: sum of all user-declared data fields.
        // No header bytes are added — the user defines the complete wire format.
        // Framing is done by TelegramIdFramer using the registered wire size and
        // the MessageId byte offset configured via .WithMessageId() on the builder.
        int totalWireSize = 0;
        foreach (var f in info.Fields) totalWireSize += f.WireSize;

        var sb = new StringBuilder(1024);
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (info.Namespace is not null)
        {
            sb.AppendLine($"namespace {info.Namespace};");
            sb.AppendLine();
        }

        sb.AppendLine($"partial class {info.ClassName} : global::PlcComLib.Telegrams.ITypedS7Telegram<{info.ClassName}>");
        sb.AppendLine("{");

        // ── static convenience members ────────────────────────────────────────
        // WireSize is the compile-time total; MessageId is the runtime value from Definition.
        sb.AppendLine($"    /// <summary>Total wire size in bytes (sum of all declared data fields).</summary>");
        sb.AppendLine($"    public static int WireSize => {totalWireSize};");
        sb.AppendLine();

        // ── ITelegram instance member ─────────────────────────────────────────
        sb.AppendLine("    /// <summary>The telegram identifier; equals <c>(ushort)Definition.MessageId</c> set via the connection builder.</summary>");
        sb.AppendLine("    public ushort TelegramId => (ushort)_s7Definition.MessageId;");
        sb.AppendLine();

        // ── Definition ────────────────────────────────────────────────────────
        sb.AppendLine("    private static readonly global::PlcComLib.Telegrams.TelegramDefinition _s7Definition = BuildS7Definition();");
        sb.AppendLine("    public static global::PlcComLib.Telegrams.TelegramDefinition Definition => _s7Definition;");
        sb.AppendLine();
        sb.AppendLine("    private static global::PlcComLib.Telegrams.TelegramDefinition BuildS7Definition() =>");
        sb.AppendLine("        new global::PlcComLib.Telegrams.TelegramDefinition");
        sb.AppendLine("        {");
        sb.AppendLine($"            Id = \"{info.ClassName}\",");
        sb.AppendLine($"            ConfiguredWireSize = {totalWireSize},");
        sb.AppendLine("            Fields =");
        sb.AppendLine("            [");
        foreach (var f in info.Fields)
        {
            sb.Append($"                new global::PlcComLib.Telegrams.TelegramField {{ Name = \"{f.Name}\", DataType = {f.DataTypeExpr}");
            if (f.Kind is S7Kind.S7String or S7Kind.S7WString)
                sb.Append($", MaxStringLength = (byte){System.Math.Min(f.MaxStringLength, 254)}");
            if (f.Kind is S7Kind.Raw or S7Kind.CharArray)
                sb.Append($", RawByteCount = {f.RawByteCount}");
            sb.AppendLine(" },");
        }
        sb.AppendLine("            ]");
        sb.AppendLine("        };");
        sb.AppendLine();

        // ── Serialize ─────────────────────────────────────────────────────────
        // Wire format: [data fields…] — exactly what the user declared, nothing prepended.
        sb.AppendLine("    public byte[] Serialize(global::PlcComLib.DataTypes.ByteOrder byteOrder = global::PlcComLib.DataTypes.ByteOrder.BigEndian)");
        sb.AppendLine("    {");
        sb.AppendLine("        bool __le = byteOrder == global::PlcComLib.DataTypes.ByteOrder.LittleEndian;");
        sb.AppendLine($"        var __buf = new byte[WireSize];");
        int offset = 0;
        foreach (var f in info.Fields)
        {
            EmitSerialize(sb, f, offset);
            offset += f.WireSize;
        }
        sb.AppendLine("        return __buf;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // ── Deserialize(ReadOnlySpan<byte>, ByteOrder) ────────────────────────
        sb.AppendLine($"    public static {info.ClassName} Deserialize(global::System.ReadOnlySpan<byte> data,");
        sb.AppendLine("        global::PlcComLib.DataTypes.ByteOrder byteOrder = global::PlcComLib.DataTypes.ByteOrder.BigEndian)");
        sb.AppendLine("    {");
        sb.AppendLine("        bool __le = byteOrder == global::PlcComLib.DataTypes.ByteOrder.LittleEndian;");
        sb.AppendLine("        if (data.Length < WireSize)");
        sb.AppendLine($"            throw new global::System.ArgumentException($\"Buffer too short: expected {{WireSize}} bytes, got {{data.Length}}.\");");
        sb.AppendLine($"        var __r = new {info.ClassName}();");
        offset = 0;
        foreach (var f in info.Fields)
        {
            EmitDeserialize(sb, f, offset);
            offset += f.WireSize;
        }
        sb.AppendLine("        return __r;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // ── Explicit interface implementation ────────────────────────────────
        sb.AppendLine("    byte[] global::PlcComLib.Telegrams.ITypedS7Telegram<" + info.ClassName + ">.Serialize(global::PlcComLib.DataTypes.ByteOrder byteOrder) => Serialize(byteOrder);");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void EmitSerialize(StringBuilder sb, FieldInfo f, int offset)
    {
        switch (f.Kind)
        {
            case S7Kind.Bool:
                sb.AppendLine($"        __buf[{offset}] = {f.Name} ? (byte)1 : (byte)0;");
                break;
            case S7Kind.Byte:
                sb.AppendLine($"        __buf[{offset}] = {f.Name};");
                break;
            case S7Kind.SInt:
                sb.AppendLine($"        __buf[{offset}] = unchecked((byte){f.Name});");
                break;
            case S7Kind.Char1:
                sb.AppendLine($"        __buf[{offset}] = (byte){f.Name};");
                break;
            case S7Kind.Word:
                sb.AppendLine($"        if (__le) global::System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(__buf.AsSpan({offset}), {f.Name}); else global::System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(__buf.AsSpan({offset}), {f.Name});");
                break;
            case S7Kind.Int:
                sb.AppendLine($"        if (__le) global::System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(__buf.AsSpan({offset}), {f.Name}); else global::System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(__buf.AsSpan({offset}), {f.Name});");
                break;
            case S7Kind.WChar:
                sb.AppendLine($"        if (__le) global::System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(__buf.AsSpan({offset}), (ushort){f.Name}); else global::System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(__buf.AsSpan({offset}), (ushort){f.Name});");
                break;
            case S7Kind.DWord:
                sb.AppendLine($"        if (__le) global::System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(__buf.AsSpan({offset}), {f.Name}); else global::System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(__buf.AsSpan({offset}), {f.Name});");
                break;
            case S7Kind.DInt:
                sb.AppendLine($"        if (__le) global::System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(__buf.AsSpan({offset}), {f.Name}); else global::System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(__buf.AsSpan({offset}), {f.Name});");
                break;
            case S7Kind.Real:
                sb.AppendLine($"        if (__le) global::System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(__buf.AsSpan({offset}), global::System.BitConverter.SingleToUInt32Bits({f.Name})); else global::PlcComLib.DataTypes.ByteSwapper.WriteReal(__buf.AsSpan({offset}), {f.Name});");
                break;
            case S7Kind.LWord:
                sb.AppendLine($"        if (__le) global::System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(__buf.AsSpan({offset}), {f.Name}); else global::System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(__buf.AsSpan({offset}), {f.Name});");
                break;
            case S7Kind.LInt:
                sb.AppendLine($"        if (__le) global::System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(__buf.AsSpan({offset}), {f.Name}); else global::System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(__buf.AsSpan({offset}), {f.Name});");
                break;
            case S7Kind.LReal:
                sb.AppendLine($"        if (__le) global::System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(__buf.AsSpan({offset}), global::System.BitConverter.DoubleToUInt64Bits({f.Name})); else global::PlcComLib.DataTypes.ByteSwapper.WriteLReal(__buf.AsSpan({offset}), {f.Name});");
                break;
            case S7Kind.DateAndTime:
                sb.AppendLine($"        {{ var __dt = global::PlcComLib.DataTypes.S7TypeConverter.Serialize(global::PlcComLib.DataTypes.S7DataType.DateAndTime, {f.Name}); __dt.CopyTo(__buf, {offset}); }}");
                break;
            case S7Kind.S7String:
                sb.AppendLine($"        new global::PlcComLib.DataTypes.S7String({f.Name} ?? \"\", {f.MaxStringLength}).WriteTo(__buf.AsSpan({offset}));");
                break;
            case S7Kind.S7WString:
                sb.AppendLine($"        new global::PlcComLib.DataTypes.S7WString({f.Name} ?? \"\", {f.MaxStringLength}).WriteTo(__buf.AsSpan({offset}));");
                break;
            case S7Kind.Raw:
                sb.AppendLine($"        ({f.Name} ?? global::System.Array.Empty<byte>()).CopyTo(__buf, {offset});");
                break;
            case S7Kind.CharArray:
                sb.AppendLine($"        {{");
                sb.AppendLine($"            var __ca = {f.Name};");
                sb.AppendLine($"            int __caLen = __ca?.Length ?? 0;");
                sb.AppendLine($"            for (int __i = 0; __i < {f.WireSize}; __i++)");
                sb.AppendLine($"                __buf[{offset} + __i] = __i < __caLen ? (byte)__ca![__i] : (byte)0;");
                sb.AppendLine($"        }}");
                break;
        }
    }

    private static void EmitDeserialize(StringBuilder sb, FieldInfo f, int offset)
    {
        switch (f.Kind)
        {
            case S7Kind.Bool:
                sb.AppendLine($"        __r.{f.Name} = data[{offset}] != 0;");
                break;
            case S7Kind.Byte:
                sb.AppendLine($"        __r.{f.Name} = data[{offset}];");
                break;
            case S7Kind.SInt:
                sb.AppendLine($"        __r.{f.Name} = (sbyte)data[{offset}];");
                break;
            case S7Kind.Char1:
                sb.AppendLine($"        __r.{f.Name} = (char)data[{offset}];");
                break;
            case S7Kind.Word:
                sb.AppendLine($"        __r.{f.Name} = __le ? global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data.Slice({offset}, 2)) : global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data.Slice({offset}, 2));");
                break;
            case S7Kind.Int:
                sb.AppendLine($"        __r.{f.Name} = __le ? global::System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(data.Slice({offset}, 2)) : global::System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(data.Slice({offset}, 2));");
                break;
            case S7Kind.WChar:
                sb.AppendLine($"        __r.{f.Name} = __le ? (char)global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data.Slice({offset}, 2)) : (char)global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data.Slice({offset}, 2));");
                break;
            case S7Kind.DWord:
                sb.AppendLine($"        __r.{f.Name} = __le ? global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.Slice({offset}, 4)) : global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.Slice({offset}, 4));");
                break;
            case S7Kind.DInt:
                sb.AppendLine($"        __r.{f.Name} = __le ? global::System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.Slice({offset}, 4)) : global::System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.Slice({offset}, 4));");
                break;
            case S7Kind.Real:
                sb.AppendLine($"        __r.{f.Name} = __le ? global::System.BitConverter.UInt32BitsToSingle(global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.Slice({offset}, 4))) : global::PlcComLib.DataTypes.ByteSwapper.ReadReal(data.Slice({offset}, 4));");
                break;
            case S7Kind.LWord:
                sb.AppendLine($"        __r.{f.Name} = __le ? global::System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(data.Slice({offset}, 8)) : global::System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(data.Slice({offset}, 8));");
                break;
            case S7Kind.LInt:
                sb.AppendLine($"        __r.{f.Name} = __le ? global::System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(data.Slice({offset}, 8)) : global::System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(data.Slice({offset}, 8));");
                break;
            case S7Kind.LReal:
                sb.AppendLine($"        __r.{f.Name} = __le ? global::System.BitConverter.UInt64BitsToDouble(global::System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(data.Slice({offset}, 8))) : global::PlcComLib.DataTypes.ByteSwapper.ReadLReal(data.Slice({offset}, 8));");
                break;
            case S7Kind.DateAndTime:
                sb.AppendLine($"        __r.{f.Name} = (global::System.DateTime)global::PlcComLib.DataTypes.S7TypeConverter.Deserialize(global::PlcComLib.DataTypes.S7DataType.DateAndTime, data.Slice({offset}, 8));");
                break;
            case S7Kind.S7String:
                sb.AppendLine($"        __r.{f.Name} = global::PlcComLib.DataTypes.S7String.ReadFrom(data.Slice({offset}, {f.WireSize})).Value;");
                break;
            case S7Kind.S7WString:
                sb.AppendLine($"        __r.{f.Name} = global::PlcComLib.DataTypes.S7WString.ReadFrom(data.Slice({offset}, {f.WireSize})).Value;");
                break;
            case S7Kind.Raw:
                sb.AppendLine($"        __r.{f.Name} = data.Slice({offset}, {f.WireSize}).ToArray();");
                break;
            case S7Kind.CharArray:
                sb.AppendLine($"        {{");
                sb.AppendLine($"            var __ca = new char[{f.WireSize}];");
                sb.AppendLine($"            for (int __i = 0; __i < {f.WireSize}; __i++)");
                sb.AppendLine($"                __ca[__i] = (char)data[{offset} + __i];");
                sb.AppendLine($"            __r.{f.Name} = __ca;");
                sb.AppendLine($"        }}");
                break;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Data models
    // ──────────────────────────────────────────────────────────────────────────

    private enum S7Kind
    {
        Bool, Byte, SInt, Char1,
        Word, Int, WChar,
        DWord, DInt, Real,
        LWord, LInt, LReal,
        DateAndTime,
        S7String, S7WString, Raw,
        CharArray
    }

    private sealed record FieldInfo(
        string Name,
        S7Kind Kind,
        string DataTypeExpr,
        int WireSize,
        int MaxStringLength,
        int RawByteCount);

    private sealed record TelegramClassInfo(
        string ClassName,
        string? Namespace,
        ImmutableArray<FieldInfo> Fields)
    {
        public string HintName =>
            Namespace is null ? ClassName : $"{Namespace}.{ClassName}";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Attribute source injected into every consuming compilation
    // ──────────────────────────────────────────────────────────────────────────

    private const string AttributeSource =
        """
        // <auto-generated />
        #nullable enable

        namespace PlcComLib.SourceGenerator;

        /// <summary>Marks a <c>partial</c> class as a strongly-typed S7 telegram.</summary>
        [System.AttributeUsage(System.AttributeTargets.Class)]
        public sealed class S7TelegramAttribute : System.Attribute { }

        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7BoolAttribute     : System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7ByteAttribute     : System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7USIntAttribute    : System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7SIntAttribute     : System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7CharAttribute     : System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7WordAttribute     : System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7UIntAttribute     : System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7IntAttribute      : System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7DateAttribute     : System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7WCharAttribute    : System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7DWordAttribute    : System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7UDIntAttribute    : System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7TimeOfDayAttribute: System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7DIntAttribute     : System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7TimeAttribute     : System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7RealAttribute     : System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7LWordAttribute    : System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7ULIntAttribute    : System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7LIntAttribute     : System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7LRealAttribute    : System.Attribute { }
        [System.AttributeUsage(System.AttributeTargets.Property)] public sealed class S7DateAndTimeAttribute : System.Attribute { }

        [System.AttributeUsage(System.AttributeTargets.Property)]
        public sealed class S7StringAttribute : System.Attribute
        {
            public S7StringAttribute(byte maxLength = 254) => MaxLength = maxLength;
            public byte MaxLength { get; }
        }

        [System.AttributeUsage(System.AttributeTargets.Property)]
        public sealed class S7WStringAttribute : System.Attribute
        {
            public S7WStringAttribute(ushort maxLength = 254) => MaxLength = maxLength;
            public ushort MaxLength { get; }
        }

        [System.AttributeUsage(System.AttributeTargets.Property)]
        public sealed class S7RawAttribute : System.Attribute
        {
            public S7RawAttribute(int byteCount) => ByteCount = byteCount;
            public int ByteCount { get; }
        }

        /// <summary>
        /// Maps a <c>char[]</c> property to a fixed-length array of S7 CHAR values on the wire.
        /// Each character occupies exactly one byte (ASCII). No length header is written.
        /// </summary>
        [System.AttributeUsage(System.AttributeTargets.Property)]
        public sealed class S7CharArrayAttribute : System.Attribute
        {
            public S7CharArrayAttribute(int length) => Length = length;
            public int Length { get; }
        }
        """;
}
