using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Extensions;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Models.AnyOf;

/// <summary>
/// Redact personally identifiable information from returned markdown. Pass <c>true</c> to use defaults, or an object to tune mode, entities, and replacement style.
/// </summary>
[JsonConverter(typeof(RedactPiiConverter))]
public record RedactPii
{
    private readonly Optional<bool> _boolValue;

    private readonly Optional<RedactPiiOptions> _redactPiiOptionsValue;

    private RedactPii(Optional<bool> boolValue, Optional<RedactPiiOptions> redactPiiOptionsValue)
    {
        _boolValue = boolValue;
        _redactPiiOptionsValue = redactPiiOptionsValue;
    }

    public static RedactPii Bool(bool value) => new(Optional<bool>.Some(value), default);

    public static RedactPii RedactPiiOptions(RedactPiiOptions value) =>
        new(default, Optional<RedactPiiOptions>.Some(value));

    public bool TryGetBool(out bool value) => _boolValue.TryGetValue(out value);

    public bool TryGetRedactPiiOptions(out RedactPiiOptions value) =>
        _redactPiiOptionsValue.TryGetValue(out value);

    public static implicit operator RedactPii(bool value) => Bool(value);

    public static implicit operator RedactPii(RedactPiiOptions value) => RedactPiiOptions(value);
}

file sealed class RedactPiiConverter : JsonConverter<RedactPii>
{
    public override RedactPii Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<bool>(root, options, out var boolValue))
        {
            return RedactPii.Bool(boolValue);
        }
        if (JsonSerializer.TryDeserialize<RedactPiiOptions>(root, options, out var redactPiiOptionsValue))
        {
            return RedactPii.RedactPiiOptions(redactPiiOptionsValue);
        }
        throw new JsonException($"JSON does not match bool or RedactPiiOptions schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, RedactPii value, JsonSerializerOptions options)
    {
        if (value.TryGetBool(out var boolValue))
        {
            JsonSerializer.Serialize(writer, boolValue, options);
        }
        else if (value.TryGetRedactPiiOptions(out var redactPiiOptionsValue))
        {
            JsonSerializer.Serialize(writer, redactPiiOptionsValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(RedactPii)} contains no valid value to serialize.");
        }
    }
}
