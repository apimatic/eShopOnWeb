using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models.OneOf;

/// <summary>
/// Request body for sending a typing indicator. The schema varies by channel. Use the <c>channel</c> field to determine which properties are required.
/// </summary>
[JsonConverter(typeof(TypingIndicatorRequestConverter))]
public record TypingIndicatorRequest
{
    private readonly Optional<MessagingV2WhatsappTypingIndicator> _messagingV2WhatsappTypingIndicatorValue;

    private readonly Optional<AppleTypingIndicatorRequest> _appleTypingIndicatorRequestValue;

    private TypingIndicatorRequest(Optional<MessagingV2WhatsappTypingIndicator> messagingV2WhatsappTypingIndicatorValue,
        Optional<AppleTypingIndicatorRequest> appleTypingIndicatorRequestValue)
    {
        _messagingV2WhatsappTypingIndicatorValue = messagingV2WhatsappTypingIndicatorValue;
        _appleTypingIndicatorRequestValue = appleTypingIndicatorRequestValue;
    }

    public static TypingIndicatorRequest MessagingV2WhatsappTypingIndicator(MessagingV2WhatsappTypingIndicator value) =>
        new(Optional<MessagingV2WhatsappTypingIndicator>.Some(value), default);

    public static TypingIndicatorRequest AppleTypingIndicatorRequest(AppleTypingIndicatorRequest value) =>
        new(default, Optional<AppleTypingIndicatorRequest>.Some(value));

    public bool TryGetMessagingV2WhatsappTypingIndicator(out MessagingV2WhatsappTypingIndicator value) =>
        _messagingV2WhatsappTypingIndicatorValue.TryGetValue(out value);

    public bool TryGetAppleTypingIndicatorRequest(out AppleTypingIndicatorRequest value) =>
        _appleTypingIndicatorRequestValue.TryGetValue(out value);

    public static implicit operator TypingIndicatorRequest(MessagingV2WhatsappTypingIndicator value) =>
        MessagingV2WhatsappTypingIndicator(value);

    public static implicit operator TypingIndicatorRequest(AppleTypingIndicatorRequest value) =>
        AppleTypingIndicatorRequest(value);
}

file sealed class TypingIndicatorRequestConverter : JsonConverter<TypingIndicatorRequest>
{
    public override TypingIndicatorRequest Read(ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (!root.TryGetProperty("channel", out var typeProperty))
        {
            throw new JsonException("Missing required 'channel' discriminator field");
        }
        var discriminator = typeProperty.GetString();
        return discriminator switch
        {
            "WHATSAPP" => TypingIndicatorRequest.MessagingV2WhatsappTypingIndicator(root.Deserialize<MessagingV2WhatsappTypingIndicator>(options)!),
            "APPLE" => TypingIndicatorRequest.AppleTypingIndicatorRequest(root.Deserialize<AppleTypingIndicatorRequest>(options)!),
            _ => throw new JsonException($"JSON does not match MessagingV2WhatsappTypingIndicator or AppleTypingIndicatorRequest schemas: {root.ToString()}")
        };
    }

    public override void Write(Utf8JsonWriter writer, TypingIndicatorRequest value, JsonSerializerOptions options)
    {
        if (value.TryGetMessagingV2WhatsappTypingIndicator(out var messagingV2WhatsappTypingIndicatorValue))
        {
            JsonSerializer.Serialize(writer, messagingV2WhatsappTypingIndicatorValue, options);
        }
        else if (value.TryGetAppleTypingIndicatorRequest(out var appleTypingIndicatorRequestValue))
        {
            JsonSerializer.Serialize(writer, appleTypingIndicatorRequestValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(TypingIndicatorRequest)} contains no valid value to serialize.");
        }
    }
}
