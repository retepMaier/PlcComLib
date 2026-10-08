using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PlcComLib.Core.Events;
using PlcComLib.DataTypes;
using PlcComLib.Framing;
using PlcComLib.Telegrams;

namespace PlcComLib.Core.Internal;

/// <summary>
/// Matches received payloads to the definitions registered on one connection, delivers them to
/// typed subscribers and <c>TelegramReceived</c>, and prepares typed telegrams for sending.
/// Shared by all TCP/UDP client and server implementations.
/// </summary>
internal sealed partial class TelegramDispatcher
{
    private readonly TelegramRegistry _registry;
    private readonly ByteOrder _byteOrder;
    private readonly ILogger _logger;

    // For each unique (offset, type) combination used as a MessageId discriminator,
    // a dictionary keyed by MessageId value → O(1) dispatch.
    private readonly (int Offset, S7DataType Type, int IdSize, Dictionary<long, TelegramDefinition> Lookup)[] _idGroups;

    // Size-based fallback for definitions without a MessageId.
    private readonly Dictionary<int, TelegramDefinition> _sizeIndex;

    private readonly Lock _subscriptionLock = new();
    private TypedSubscription[] _subscriptions = [];

    private sealed record TypedSubscription(TelegramDefinition Definition, string TypeName, Action<byte[], string, int> Deliver);

    public TelegramDispatcher(TelegramRegistry registry, ByteOrder byteOrder, ILogger? logger)
    {
        _registry  = registry ?? throw new ArgumentNullException(nameof(registry));
        _byteOrder = byteOrder;
        _logger    = logger ?? NullLogger.Instance;

        _idGroups = registry.Definitions
            .Where(d => d.MessageId != 0)
            .GroupBy(d => (d.MessageIdByteOffset, d.MessageIdDataType))
            .Select(g => (g.Key.MessageIdByteOffset, g.Key.MessageIdDataType,
                          S7TypeConverter.GetWireSize(g.Key.MessageIdDataType),
                          g.ToDictionary(d => d.MessageId)))
            .ToArray();

        _sizeIndex = registry.Definitions
            .Where(d => d.MessageId == 0 && d.EffectiveWireSize > 0)
            .GroupBy(d => d.EffectiveWireSize)
            .ToDictionary(g => g.Key, g => g.First());
    }

    // ── Receive ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds the registered definition for <paramref name="payload"/> and validates its length field.
    /// Returns <c>null</c> (after logging) when the payload is unknown or fails validation;
    /// the caller then raises <c>UnknownTelegramReceived</c>.
    /// </summary>
    public TelegramDefinition? Match(byte[] payload, string source)
    {
        foreach (var (offset, type, idSize, lookup) in _idGroups)
        {
            if (payload.Length < offset + idSize) continue;
            long id = TelegramIdFramer.ReadId(payload, offset, type, _byteOrder);
            if (!lookup.TryGetValue(id, out var def)) continue;
            return ValidateLength(def, payload, source) ? def : null;
        }

        if (_sizeIndex.TryGetValue(payload.Length, out var sizeDef))
            return ValidateLength(sizeDef, payload, source) ? sizeDef : null;

        LogNoMatchingDefinition(_logger, payload.Length, source);
        return null;
    }

    private bool ValidateLength(TelegramDefinition def, byte[] payload, string source)
    {
        if (def.LengthByteOffset < 0) return true;

        int fieldEnd = def.LengthByteOffset + S7TypeConverter.GetWireSize(def.LengthDataType);
        if (payload.Length < fieldEnd)
        {
            LogLengthFieldBeyondPayload(_logger, def.Id, def.LengthByteOffset, payload.Length, source);
            return false;
        }

        long receivedLength = TelegramIdFramer.ReadLength(payload, def.LengthByteOffset, def.LengthDataType, _byteOrder);
        if (receivedLength != def.ConfiguredWireSize)
        {
            LogLengthFieldMismatch(_logger, def.Id, def.ConfiguredWireSize, receivedLength, source);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Delivers a matched payload: first to typed subscribers of <paramref name="definition"/>
    /// (each decoded with its own typed deserializer), then to <paramref name="telegramReceived"/>.
    /// A failure in one path never prevents the other.
    /// </summary>
    public void Deliver(
        TelegramDefinition definition,
        byte[] payload,
        string remoteAddress,
        int port,
        object sender,
        EventHandler<TelegramReceivedEventArgs>? telegramReceived)
    {
        foreach (var subscription in Volatile.Read(ref _subscriptions))
        {
            if (!ReferenceEquals(subscription.Definition, definition)) continue;
            try { subscription.Deliver(payload, remoteAddress, port); }
            catch (Exception ex) { LogTypedHandlerFailed(_logger, ex, subscription.TypeName); }
        }

        if (telegramReceived is null) return;

        Telegram telegram;
        try { telegram = TelegramSerializer.Deserialize(definition, payload, _byteOrder); }
        catch (Exception ex)
        {
            LogDeserializeFailed(_logger, ex, definition.Id, remoteAddress, port);
            return;
        }
        EventRaiser.Raise(telegramReceived, sender, new TelegramReceivedEventArgs(telegram, payload, remoteAddress, port), _logger);
    }

    // ── Typed subscriptions ───────────────────────────────────────────────────

    /// <summary>
    /// Registers <paramref name="handler"/> for telegrams of type <typeparamref name="T"/>.
    /// Matching uses this connection's registered copy of the definition, so the MessageId
    /// configured on this connection's builder is honoured.
    /// </summary>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> is not registered on this connection.</exception>
    public IDisposable Subscribe<T>(Action<T, string, int> handler) where T : ITypedS7Telegram<T>
    {
        ArgumentNullException.ThrowIfNull(handler);
        var definition = GetRegisteredDefinition<T>()
            ?? throw new InvalidOperationException(
                $"Telegram type '{typeof(T).Name}' is not registered on this connection. " +
                $"Call RegisterTelegram<{typeof(T).Name}>() on the builder.");

        var byteOrder = _byteOrder;
        var subscription = new TypedSubscription(
            definition,
            typeof(T).Name,
            (payload, address, port) => handler(T.Deserialize(payload, byteOrder), address, port));

        lock (_subscriptionLock) _subscriptions = [.. _subscriptions, subscription];

        return new Unsubscriber(() =>
        {
            lock (_subscriptionLock)
                _subscriptions = [.. _subscriptions.Where(s => !ReferenceEquals(s, subscription))];
        });
    }

    // ── Send ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Serialises <paramref name="telegram"/> and writes the MessageId and length value configured
    /// on this connection into their header fields, so the receiver can always identify the frame.
    /// </summary>
    public byte[] Serialize<T>(T telegram) where T : ITypedS7Telegram<T>
    {
        var payload = telegram.Serialize(_byteOrder);
        if (GetRegisteredDefinition<T>() is { } def)
        {
            if (def.MessageId != 0)
                TelegramIdFramer.TryWriteInteger(payload, def.MessageIdByteOffset, def.MessageIdDataType, def.MessageId, _byteOrder);
            if (def.LengthByteOffset >= 0)
                TelegramIdFramer.TryWriteInteger(payload, def.LengthByteOffset, def.LengthDataType, def.ConfiguredWireSize, _byteOrder);
        }
        return payload;
    }

    private TelegramDefinition? GetRegisteredDefinition<T>() where T : ITypedS7Telegram<T>
        => _registry.TryGet(T.Definition.Id, out var def) ? def : null;

    private sealed class Unsubscriber(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;
        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }

    // ── Logging ───────────────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Warning, Message = "No matching telegram definition for payload of {Length} bytes from {Source}.")]
    private static partial void LogNoMatchingDefinition(ILogger logger, int length, string source);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram '{Id}' from {Source}: length field at offset {Offset} extends beyond payload ({PayloadLen} bytes).")]
    private static partial void LogLengthFieldBeyondPayload(ILogger logger, string id, int offset, int payloadLen, string source);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram '{Id}' from {Source}: length field mismatch — expected {Expected}, got {Received}.")]
    private static partial void LogLengthFieldMismatch(ILogger logger, string id, long expected, long received, string source);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Typed handler for {TypeName} failed.")]
    private static partial void LogTypedHandlerFailed(ILogger logger, Exception ex, string typeName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deserialize telegram '{Id}' from {RemoteAddress}:{Port}.")]
    private static partial void LogDeserializeFailed(ILogger logger, Exception ex, string id, string remoteAddress, int port);
}
