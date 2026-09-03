using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Extensions;
using Maxio.Core.Models;

namespace Maxio.Models.AnyOf;

/// <summary>
/// An array of segment uids to refund or the string 'all' to indicate that all segments should be refunded
/// </summary>
[JsonConverter(typeof(SegmentUidsConverter))]
public record SegmentUids
{
    private readonly Optional<IReadOnlyList<string>> _listOfStringValue;

    private readonly Optional<string> _stringValue;

    private SegmentUids(Optional<IReadOnlyList<string>> listOfStringValue, Optional<string> stringValue)
    {
        _listOfStringValue = listOfStringValue;
        _stringValue = stringValue;
    }

    public static SegmentUids ListOfString(IReadOnlyList<string> value) =>
        new(Optional<IReadOnlyList<string>>.Some(value), default);

    public static SegmentUids String(string value) => new(default, Optional<string>.Some(value));

    public bool TryGetListOfString(out IReadOnlyList<string> value) =>
        _listOfStringValue.TryGetValue(out value);

    public bool TryGetString(out string value) => _stringValue.TryGetValue(out value);

    public static implicit operator SegmentUids(string value) => String(value);
}

file sealed class SegmentUidsConverter : JsonConverter<SegmentUids>
{
    public override SegmentUids Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<IReadOnlyList<string>>(root, options, out var listOfStringValue))
        {
            return SegmentUids.ListOfString(listOfStringValue);
        }
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString()!;
            return SegmentUids.String(value);
        }
        throw new JsonException($"JSON does not match IReadOnlyList<string> or string schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, SegmentUids value, JsonSerializerOptions options)
    {
        if (value.TryGetListOfString(out var listOfStringValue))
        {
            JsonSerializer.Serialize(writer, listOfStringValue, options);
        }
        else if (value.TryGetString(out var stringValue))
        {
            JsonSerializer.Serialize(writer, stringValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(SegmentUids)} contains no valid value to serialize.");
        }
    }
}
