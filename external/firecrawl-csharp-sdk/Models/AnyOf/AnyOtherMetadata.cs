using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Extensions;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Models.AnyOf;

/// <summary>
/// Other metadata extracted from HTML, can be a string or array of strings
/// </summary>
[JsonConverter(typeof(AnyOtherMetadataConverter))]
public record AnyOtherMetadata
{
    private readonly Optional<string> _stringValue;

    private readonly Optional<IReadOnlyList<string>> _listOfStringValue;

    private AnyOtherMetadata(Optional<string> stringValue, Optional<IReadOnlyList<string>> listOfStringValue)
    {
        _stringValue = stringValue;
        _listOfStringValue = listOfStringValue;
    }

    public static AnyOtherMetadata String(string value) => new(Optional<string>.Some(value), default);

    public static AnyOtherMetadata ListOfString(IReadOnlyList<string> value) =>
        new(default, Optional<IReadOnlyList<string>>.Some(value));

    public bool TryGetString(out string value) => _stringValue.TryGetValue(out value);

    public bool TryGetListOfString(out IReadOnlyList<string> value) =>
        _listOfStringValue.TryGetValue(out value);

    public static implicit operator AnyOtherMetadata(string value) => String(value);
}

file sealed class AnyOtherMetadataConverter : JsonConverter<AnyOtherMetadata>
{
    public override AnyOtherMetadata Read(ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString()!;
            return AnyOtherMetadata.String(value);
        }
        if (JsonSerializer.TryDeserialize<IReadOnlyList<string>>(root, options, out var listOfStringValue))
        {
            return AnyOtherMetadata.ListOfString(listOfStringValue);
        }
        throw new JsonException($"JSON does not match string or IReadOnlyList<string> schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, AnyOtherMetadata value, JsonSerializerOptions options)
    {
        if (value.TryGetString(out var stringValue))
        {
            JsonSerializer.Serialize(writer, stringValue, options);
        }
        else if (value.TryGetListOfString(out var listOfStringValue))
        {
            JsonSerializer.Serialize(writer, listOfStringValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(AnyOtherMetadata)} contains no valid value to serialize.");
        }
    }
}
