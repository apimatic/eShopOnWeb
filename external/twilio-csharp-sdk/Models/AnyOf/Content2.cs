using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Twilio.Core.Extensions;
using Twilio.Core.Models;

namespace Twilio.Models.AnyOf;

/// <summary>
/// The content of the Communication.
/// </summary>
[JsonConverter(typeof(Content2Converter))]
public record Content2
{
    private readonly Optional<ContentText1> _contentText1Value;

    private readonly Optional<ContentTranscription1> _contentTranscription1Value;

    private Content2(Optional<ContentText1> contentText1Value,
        Optional<ContentTranscription1> contentTranscription1Value)
    {
        _contentText1Value = contentText1Value;
        _contentTranscription1Value = contentTranscription1Value;
    }

    public static Content2 ContentText1(ContentText1 value) =>
        new(Optional<ContentText1>.Some(value), default);

    public static Content2 ContentTranscription1(ContentTranscription1 value) =>
        new(default, Optional<ContentTranscription1>.Some(value));

    public bool TryGetContentText1(out ContentText1 value) => _contentText1Value.TryGetValue(out value);

    public bool TryGetContentTranscription1(out ContentTranscription1 value) =>
        _contentTranscription1Value.TryGetValue(out value);

    public static implicit operator Content2(ContentText1 value) => ContentText1(value);

    public static implicit operator Content2(ContentTranscription1 value) => ContentTranscription1(value);
}

file sealed class Content2Converter : JsonConverter<Content2>
{
    public override Content2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<ContentText1>(root, options, out var contentText1Value))
        {
            return Content2.ContentText1(contentText1Value);
        }
        if (JsonSerializer.TryDeserialize<ContentTranscription1>(root, options, out var contentTranscription1Value))
        {
            return Content2.ContentTranscription1(contentTranscription1Value);
        }
        throw new JsonException($"JSON does not match ContentText1 or ContentTranscription1 schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, Content2 value, JsonSerializerOptions options)
    {
        if (value.TryGetContentText1(out var contentText1Value))
        {
            JsonSerializer.Serialize(writer, contentText1Value, options);
        }
        else if (value.TryGetContentTranscription1(out var contentTranscription1Value))
        {
            JsonSerializer.Serialize(writer, contentTranscription1Value, options);
        }
        else
        {
            throw new JsonException($"{nameof(Content2)} contains no valid value to serialize.");
        }
    }
}
