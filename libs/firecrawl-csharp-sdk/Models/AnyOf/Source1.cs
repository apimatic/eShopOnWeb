using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Extensions;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Models.AnyOf;

[JsonConverter(typeof(Source1Converter))]
public record Source1
{
    private readonly Optional<Web> _webValue;

    private readonly Optional<Images> _imagesValue;

    private readonly Optional<News> _newsValue;

    private Source1(Optional<Web> webValue, Optional<Images> imagesValue, Optional<News> newsValue)
    {
        _webValue = webValue;
        _imagesValue = imagesValue;
        _newsValue = newsValue;
    }

    public static Source1 Web(Web value) => new(Optional<Web>.Some(value), default, default);

    public static Source1 Images(Images value) => new(default, Optional<Images>.Some(value), default);

    public static Source1 News(News value) => new(default, default, Optional<News>.Some(value));

    public bool TryGetWeb(out Web value) => _webValue.TryGetValue(out value);

    public bool TryGetImages(out Images value) => _imagesValue.TryGetValue(out value);

    public bool TryGetNews(out News value) => _newsValue.TryGetValue(out value);

    public static implicit operator Source1(Web value) => Web(value);

    public static implicit operator Source1(Images value) => Images(value);

    public static implicit operator Source1(News value) => News(value);
}

file sealed class Source1Converter : JsonConverter<Source1>
{
    public override Source1 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<Web>(root, options, out var webValue))
        {
            return Source1.Web(webValue);
        }
        if (JsonSerializer.TryDeserialize<Images>(root, options, out var imagesValue))
        {
            return Source1.Images(imagesValue);
        }
        if (JsonSerializer.TryDeserialize<News>(root, options, out var newsValue))
        {
            return Source1.News(newsValue);
        }
        throw new JsonException($"JSON does not match Web or Images or News schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, Source1 value, JsonSerializerOptions options)
    {
        if (value.TryGetWeb(out var webValue))
        {
            JsonSerializer.Serialize(writer, webValue, options);
        }
        else if (value.TryGetImages(out var imagesValue))
        {
            JsonSerializer.Serialize(writer, imagesValue, options);
        }
        else if (value.TryGetNews(out var newsValue))
        {
            JsonSerializer.Serialize(writer, newsValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(Source1)} contains no valid value to serialize.");
        }
    }
}
