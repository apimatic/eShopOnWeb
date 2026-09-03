using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Twilio.Core.Extensions;
using Twilio.Core.Models;

namespace Twilio.Models.AnyOf;

[JsonConverter(typeof(MessagingV1ServiceUsAppToPersonResponseConverter))]
public record MessagingV1ServiceUsAppToPersonResponse
{
    private readonly Optional<MessagingV1ServiceUsAppToPerson> _messagingV1ServiceUsAppToPersonValue;

    private readonly Optional<MessagingV1ServiceUsAppToPersonV2> _messagingV1ServiceUsAppToPersonV2Value;

    private MessagingV1ServiceUsAppToPersonResponse(Optional<MessagingV1ServiceUsAppToPerson> messagingV1ServiceUsAppToPersonValue,
        Optional<MessagingV1ServiceUsAppToPersonV2> messagingV1ServiceUsAppToPersonV2Value)
    {
        _messagingV1ServiceUsAppToPersonValue = messagingV1ServiceUsAppToPersonValue;
        _messagingV1ServiceUsAppToPersonV2Value = messagingV1ServiceUsAppToPersonV2Value;
    }

    public static MessagingV1ServiceUsAppToPersonResponse MessagingV1ServiceUsAppToPerson(MessagingV1ServiceUsAppToPerson value) =>
        new(Optional<MessagingV1ServiceUsAppToPerson>.Some(value), default);

    public static MessagingV1ServiceUsAppToPersonResponse MessagingV1ServiceUsAppToPersonV2(MessagingV1ServiceUsAppToPersonV2 value) =>
        new(default, Optional<MessagingV1ServiceUsAppToPersonV2>.Some(value));

    public bool TryGetMessagingV1ServiceUsAppToPerson(out MessagingV1ServiceUsAppToPerson value) =>
        _messagingV1ServiceUsAppToPersonValue.TryGetValue(out value);

    public bool TryGetMessagingV1ServiceUsAppToPersonV2(out MessagingV1ServiceUsAppToPersonV2 value) =>
        _messagingV1ServiceUsAppToPersonV2Value.TryGetValue(out value);

    public static implicit operator MessagingV1ServiceUsAppToPersonResponse(MessagingV1ServiceUsAppToPerson value) =>
        MessagingV1ServiceUsAppToPerson(value);

    public static implicit operator MessagingV1ServiceUsAppToPersonResponse(MessagingV1ServiceUsAppToPersonV2 value) =>
        MessagingV1ServiceUsAppToPersonV2(value);
}

file sealed class MessagingV1ServiceUsAppToPersonResponseConverter : JsonConverter<MessagingV1ServiceUsAppToPersonResponse>
{
    public override MessagingV1ServiceUsAppToPersonResponse Read(ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<MessagingV1ServiceUsAppToPerson>(root,
            options,
            out var messagingV1ServiceUsAppToPersonValue))
        {
            return MessagingV1ServiceUsAppToPersonResponse.MessagingV1ServiceUsAppToPerson(messagingV1ServiceUsAppToPersonValue);
        }
        if (JsonSerializer.TryDeserialize<MessagingV1ServiceUsAppToPersonV2>(root,
            options,
            out var messagingV1ServiceUsAppToPersonV2Value))
        {
            return MessagingV1ServiceUsAppToPersonResponse.MessagingV1ServiceUsAppToPersonV2(messagingV1ServiceUsAppToPersonV2Value);
        }
        throw new JsonException($"JSON does not match MessagingV1ServiceUsAppToPerson or MessagingV1ServiceUsAppToPersonV2 schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer,
        MessagingV1ServiceUsAppToPersonResponse value,
        JsonSerializerOptions options)
    {
        if (value.TryGetMessagingV1ServiceUsAppToPerson(out var messagingV1ServiceUsAppToPersonValue))
        {
            JsonSerializer.Serialize(writer, messagingV1ServiceUsAppToPersonValue, options);
        }
        else if (value.TryGetMessagingV1ServiceUsAppToPersonV2(out var messagingV1ServiceUsAppToPersonV2Value))
        {
            JsonSerializer.Serialize(writer, messagingV1ServiceUsAppToPersonV2Value, options);
        }
        else
        {
            throw new JsonException($"{nameof(MessagingV1ServiceUsAppToPersonResponse)} contains no valid value to serialize.");
        }
    }
}
