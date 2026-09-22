using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Extensions;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models.AnyOf;

/// <summary>
/// The content of the Communication using type field for discrimination.
/// </summary>
[JsonConverter(typeof(ContentConverter))]
public record Content
{
    private readonly Optional<ContentText> _contentTextValue;

    private readonly Optional<ContentTranscription> _contentTranscriptionValue;

    private Content(Optional<ContentText> contentTextValue,
        Optional<ContentTranscription> contentTranscriptionValue)
    {
        _contentTextValue = contentTextValue;
        _contentTranscriptionValue = contentTranscriptionValue;
    }

    public static Content ContentText(ContentText value) => new(Optional<ContentText>.Some(value), default);

    public static Content ContentTranscription(ContentTranscription value) =>
        new(default, Optional<ContentTranscription>.Some(value));

    public bool TryGetContentText(out ContentText value) => _contentTextValue.TryGetValue(out value);

    public bool TryGetContentTranscription(out ContentTranscription value) =>
        _contentTranscriptionValue.TryGetValue(out value);

    public static implicit operator Content(ContentText value) => ContentText(value);

    public static implicit operator Content(ContentTranscription value) => ContentTranscription(value);
}

file sealed class ContentConverter : JsonConverter<Content>
{
    public override Content Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<ContentText>(root, options, out var contentTextValue))
        {
            return Content.ContentText(contentTextValue);
        }
        if (JsonSerializer.TryDeserialize<ContentTranscription>(root, options, out var contentTranscriptionValue))
        {
            return Content.ContentTranscription(contentTranscriptionValue);
        }
        throw new JsonException($"JSON does not match ContentText or ContentTranscription schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, Content value, JsonSerializerOptions options)
    {
        if (value.TryGetContentText(out var contentTextValue))
        {
            JsonSerializer.Serialize(writer, contentTextValue, options);
        }
        else if (value.TryGetContentTranscription(out var contentTranscriptionValue))
        {
            JsonSerializer.Serialize(writer, contentTranscriptionValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(Content)} contains no valid value to serialize.");
        }
    }
}
