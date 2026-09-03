using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Extensions;
using Maxio.Core.Models;

namespace Maxio.Models.AnyOf;

[JsonConverter(typeof(Errors11Converter))]
public record Errors11
{
    private readonly Optional<SubscriptionGroupMembersArrayError> _subscriptionGroupMembersArrayErrorValue;

    private readonly Optional<SubscriptionGroupSingleError> _subscriptionGroupSingleErrorValue;

    private readonly Optional<string> _stringValue;

    private Errors11(Optional<SubscriptionGroupMembersArrayError> subscriptionGroupMembersArrayErrorValue,
        Optional<SubscriptionGroupSingleError> subscriptionGroupSingleErrorValue,
        Optional<string> stringValue)
    {
        _subscriptionGroupMembersArrayErrorValue = subscriptionGroupMembersArrayErrorValue;
        _subscriptionGroupSingleErrorValue = subscriptionGroupSingleErrorValue;
        _stringValue = stringValue;
    }

    public static Errors11 SubscriptionGroupMembersArrayError(SubscriptionGroupMembersArrayError value) =>
        new(Optional<SubscriptionGroupMembersArrayError>.Some(value), default, default);

    public static Errors11 SubscriptionGroupSingleError(SubscriptionGroupSingleError value) =>
        new(default, Optional<SubscriptionGroupSingleError>.Some(value), default);

    public static Errors11 String(string value) => new(default, default, Optional<string>.Some(value));

    public bool TryGetSubscriptionGroupMembersArrayError(out SubscriptionGroupMembersArrayError value) =>
        _subscriptionGroupMembersArrayErrorValue.TryGetValue(out value);

    public bool TryGetSubscriptionGroupSingleError(out SubscriptionGroupSingleError value) =>
        _subscriptionGroupSingleErrorValue.TryGetValue(out value);

    public bool TryGetString(out string value) => _stringValue.TryGetValue(out value);

    public static implicit operator Errors11(SubscriptionGroupMembersArrayError value) =>
        SubscriptionGroupMembersArrayError(value);

    public static implicit operator Errors11(SubscriptionGroupSingleError value) =>
        SubscriptionGroupSingleError(value);

    public static implicit operator Errors11(string value) => String(value);
}

file sealed class Errors11Converter : JsonConverter<Errors11>
{
    public override Errors11 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<SubscriptionGroupMembersArrayError>(root,
            options,
            out var subscriptionGroupMembersArrayErrorValue))
        {
            return Errors11.SubscriptionGroupMembersArrayError(subscriptionGroupMembersArrayErrorValue);
        }
        if (JsonSerializer.TryDeserialize<SubscriptionGroupSingleError>(root,
            options,
            out var subscriptionGroupSingleErrorValue))
        {
            return Errors11.SubscriptionGroupSingleError(subscriptionGroupSingleErrorValue);
        }
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString()!;
            return Errors11.String(value);
        }
        throw new JsonException($"JSON does not match SubscriptionGroupMembersArrayError or SubscriptionGroupSingleError or string schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, Errors11 value, JsonSerializerOptions options)
    {
        if (value.TryGetSubscriptionGroupMembersArrayError(out var subscriptionGroupMembersArrayErrorValue))
        {
            JsonSerializer.Serialize(writer, subscriptionGroupMembersArrayErrorValue, options);
        }
        else if (value.TryGetSubscriptionGroupSingleError(out var subscriptionGroupSingleErrorValue))
        {
            JsonSerializer.Serialize(writer, subscriptionGroupSingleErrorValue, options);
        }
        else if (value.TryGetString(out var stringValue))
        {
            JsonSerializer.Serialize(writer, stringValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(Errors11)} contains no valid value to serialize.");
        }
    }
}
