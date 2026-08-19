using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Extensions;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Models.AnyOf;

[JsonConverter(typeof(ParseFormatsConverter))]
public record ParseFormats
{
    private readonly Optional<Markdown> _markdownValue;

    private readonly Optional<Summary> _summaryValue;

    private readonly Optional<Html> _htmlValue;

    private readonly Optional<RawHtml> _rawHtmlValue;

    private readonly Optional<Links> _linksValue;

    private readonly Optional<Images> _imagesValue;

    private readonly Optional<Json> _jsonValue;

    private ParseFormats(Optional<Markdown> markdownValue,
        Optional<Summary> summaryValue,
        Optional<Html> htmlValue,
        Optional<RawHtml> rawHtmlValue,
        Optional<Links> linksValue,
        Optional<Images> imagesValue,
        Optional<Json> jsonValue)
    {
        _markdownValue = markdownValue;
        _summaryValue = summaryValue;
        _htmlValue = htmlValue;
        _rawHtmlValue = rawHtmlValue;
        _linksValue = linksValue;
        _imagesValue = imagesValue;
        _jsonValue = jsonValue;
    }

    public static ParseFormats Markdown(Markdown value) =>
        new(Optional<Markdown>.Some(value), default, default, default, default, default, default);

    public static ParseFormats Summary(Summary value) =>
        new(default, Optional<Summary>.Some(value), default, default, default, default, default);

    public static ParseFormats Html(Html value) =>
        new(default, default, Optional<Html>.Some(value), default, default, default, default);

    public static ParseFormats RawHtml(RawHtml value) =>
        new(default, default, default, Optional<RawHtml>.Some(value), default, default, default);

    public static ParseFormats Links(Links value) =>
        new(default, default, default, default, Optional<Links>.Some(value), default, default);

    public static ParseFormats Images(Images value) =>
        new(default, default, default, default, default, Optional<Images>.Some(value), default);

    public static ParseFormats Json(Json value) =>
        new(default, default, default, default, default, default, Optional<Json>.Some(value));

    public bool TryGetMarkdown(out Markdown value) => _markdownValue.TryGetValue(out value);

    public bool TryGetSummary(out Summary value) => _summaryValue.TryGetValue(out value);

    public bool TryGetHtml(out Html value) => _htmlValue.TryGetValue(out value);

    public bool TryGetRawHtml(out RawHtml value) => _rawHtmlValue.TryGetValue(out value);

    public bool TryGetLinks(out Links value) => _linksValue.TryGetValue(out value);

    public bool TryGetImages(out Images value) => _imagesValue.TryGetValue(out value);

    public bool TryGetJson(out Json value) => _jsonValue.TryGetValue(out value);

    public static implicit operator ParseFormats(Markdown value) => Markdown(value);

    public static implicit operator ParseFormats(Summary value) => Summary(value);

    public static implicit operator ParseFormats(Html value) => Html(value);

    public static implicit operator ParseFormats(RawHtml value) => RawHtml(value);

    public static implicit operator ParseFormats(Links value) => Links(value);

    public static implicit operator ParseFormats(Images value) => Images(value);

    public static implicit operator ParseFormats(Json value) => Json(value);
}

file sealed class ParseFormatsConverter : JsonConverter<ParseFormats>
{
    public override ParseFormats Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<Markdown>(root, options, out var markdownValue))
        {
            return ParseFormats.Markdown(markdownValue);
        }
        if (JsonSerializer.TryDeserialize<Summary>(root, options, out var summaryValue))
        {
            return ParseFormats.Summary(summaryValue);
        }
        if (JsonSerializer.TryDeserialize<Html>(root, options, out var htmlValue))
        {
            return ParseFormats.Html(htmlValue);
        }
        if (JsonSerializer.TryDeserialize<RawHtml>(root, options, out var rawHtmlValue))
        {
            return ParseFormats.RawHtml(rawHtmlValue);
        }
        if (JsonSerializer.TryDeserialize<Links>(root, options, out var linksValue))
        {
            return ParseFormats.Links(linksValue);
        }
        if (JsonSerializer.TryDeserialize<Images>(root, options, out var imagesValue))
        {
            return ParseFormats.Images(imagesValue);
        }
        if (JsonSerializer.TryDeserialize<Json>(root, options, out var jsonValue))
        {
            return ParseFormats.Json(jsonValue);
        }
        throw new JsonException($"JSON does not match Markdown or Summary or Html or RawHtml or Links or Images or Json schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, ParseFormats value, JsonSerializerOptions options)
    {
        if (value.TryGetMarkdown(out var markdownValue))
        {
            JsonSerializer.Serialize(writer, markdownValue, options);
        }
        else if (value.TryGetSummary(out var summaryValue))
        {
            JsonSerializer.Serialize(writer, summaryValue, options);
        }
        else if (value.TryGetHtml(out var htmlValue))
        {
            JsonSerializer.Serialize(writer, htmlValue, options);
        }
        else if (value.TryGetRawHtml(out var rawHtmlValue))
        {
            JsonSerializer.Serialize(writer, rawHtmlValue, options);
        }
        else if (value.TryGetLinks(out var linksValue))
        {
            JsonSerializer.Serialize(writer, linksValue, options);
        }
        else if (value.TryGetImages(out var imagesValue))
        {
            JsonSerializer.Serialize(writer, imagesValue, options);
        }
        else if (value.TryGetJson(out var jsonValue))
        {
            JsonSerializer.Serialize(writer, jsonValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(ParseFormats)} contains no valid value to serialize.");
        }
    }
}
