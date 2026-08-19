using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Extensions;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Models.AnyOf;

[JsonConverter(typeof(FormatsConverter))]
public record Formats
{
    private readonly Optional<Markdown> _markdownValue;

    private readonly Optional<Summary> _summaryValue;

    private readonly Optional<Html> _htmlValue;

    private readonly Optional<RawHtml> _rawHtmlValue;

    private readonly Optional<Links> _linksValue;

    private readonly Optional<Images> _imagesValue;

    private readonly Optional<Screenshot> _screenshotValue;

    private readonly Optional<Json> _jsonValue;

    private readonly Optional<ChangeTracking> _changeTrackingValue;

    private readonly Optional<Branding> _brandingValue;

    private readonly Optional<Product> _productValue;

    private readonly Optional<Menu> _menuValue;

    private readonly Optional<Audio> _audioValue;

    private readonly Optional<Video> _videoValue;

    private readonly Optional<Question> _questionValue;

    private readonly Optional<Highlights> _highlightsValue;

    private Formats(Optional<Markdown> markdownValue,
        Optional<Summary> summaryValue,
        Optional<Html> htmlValue,
        Optional<RawHtml> rawHtmlValue,
        Optional<Links> linksValue,
        Optional<Images> imagesValue,
        Optional<Screenshot> screenshotValue,
        Optional<Json> jsonValue,
        Optional<ChangeTracking> changeTrackingValue,
        Optional<Branding> brandingValue,
        Optional<Product> productValue,
        Optional<Menu> menuValue,
        Optional<Audio> audioValue,
        Optional<Video> videoValue,
        Optional<Question> questionValue,
        Optional<Highlights> highlightsValue)
    {
        _markdownValue = markdownValue;
        _summaryValue = summaryValue;
        _htmlValue = htmlValue;
        _rawHtmlValue = rawHtmlValue;
        _linksValue = linksValue;
        _imagesValue = imagesValue;
        _screenshotValue = screenshotValue;
        _jsonValue = jsonValue;
        _changeTrackingValue = changeTrackingValue;
        _brandingValue = brandingValue;
        _productValue = productValue;
        _menuValue = menuValue;
        _audioValue = audioValue;
        _videoValue = videoValue;
        _questionValue = questionValue;
        _highlightsValue = highlightsValue;
    }

    public static Formats Markdown(Markdown value) =>
        new(Optional<Markdown>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static Formats Summary(Summary value) =>
        new(default,
            Optional<Summary>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static Formats Html(Html value) =>
        new(default,
            default,
            Optional<Html>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static Formats RawHtml(RawHtml value) =>
        new(default,
            default,
            default,
            Optional<RawHtml>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static Formats Links(Links value) =>
        new(default,
            default,
            default,
            default,
            Optional<Links>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static Formats Images(Images value) =>
        new(default,
            default,
            default,
            default,
            default,
            Optional<Images>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static Formats Screenshot(Screenshot value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            Optional<Screenshot>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static Formats Json(Json value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<Json>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static Formats ChangeTracking(ChangeTracking value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<ChangeTracking>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static Formats Branding(Branding value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<Branding>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default);

    public static Formats Product(Product value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<Product>.Some(value),
            default,
            default,
            default,
            default,
            default);

    public static Formats Menu(Menu value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<Menu>.Some(value),
            default,
            default,
            default,
            default);

    public static Formats Audio(Audio value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<Audio>.Some(value),
            default,
            default,
            default);

    public static Formats Video(Video value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<Video>.Some(value),
            default,
            default);

    public static Formats Question(Question value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<Question>.Some(value),
            default);

    public static Formats Highlights(Highlights value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<Highlights>.Some(value));

    public bool TryGetMarkdown(out Markdown value) => _markdownValue.TryGetValue(out value);

    public bool TryGetSummary(out Summary value) => _summaryValue.TryGetValue(out value);

    public bool TryGetHtml(out Html value) => _htmlValue.TryGetValue(out value);

    public bool TryGetRawHtml(out RawHtml value) => _rawHtmlValue.TryGetValue(out value);

    public bool TryGetLinks(out Links value) => _linksValue.TryGetValue(out value);

    public bool TryGetImages(out Images value) => _imagesValue.TryGetValue(out value);

    public bool TryGetScreenshot(out Screenshot value) => _screenshotValue.TryGetValue(out value);

    public bool TryGetJson(out Json value) => _jsonValue.TryGetValue(out value);

    public bool TryGetChangeTracking(out ChangeTracking value) => _changeTrackingValue.TryGetValue(out value);

    public bool TryGetBranding(out Branding value) => _brandingValue.TryGetValue(out value);

    public bool TryGetProduct(out Product value) => _productValue.TryGetValue(out value);

    public bool TryGetMenu(out Menu value) => _menuValue.TryGetValue(out value);

    public bool TryGetAudio(out Audio value) => _audioValue.TryGetValue(out value);

    public bool TryGetVideo(out Video value) => _videoValue.TryGetValue(out value);

    public bool TryGetQuestion(out Question value) => _questionValue.TryGetValue(out value);

    public bool TryGetHighlights(out Highlights value) => _highlightsValue.TryGetValue(out value);

    public static implicit operator Formats(Markdown value) => Markdown(value);

    public static implicit operator Formats(Summary value) => Summary(value);

    public static implicit operator Formats(Html value) => Html(value);

    public static implicit operator Formats(RawHtml value) => RawHtml(value);

    public static implicit operator Formats(Links value) => Links(value);

    public static implicit operator Formats(Images value) => Images(value);

    public static implicit operator Formats(Screenshot value) => Screenshot(value);

    public static implicit operator Formats(Json value) => Json(value);

    public static implicit operator Formats(ChangeTracking value) => ChangeTracking(value);

    public static implicit operator Formats(Branding value) => Branding(value);

    public static implicit operator Formats(Product value) => Product(value);

    public static implicit operator Formats(Menu value) => Menu(value);

    public static implicit operator Formats(Audio value) => Audio(value);

    public static implicit operator Formats(Video value) => Video(value);

    public static implicit operator Formats(Question value) => Question(value);

    public static implicit operator Formats(Highlights value) => Highlights(value);
}

file sealed class FormatsConverter : JsonConverter<Formats>
{
    public override Formats Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<Markdown>(root, options, out var markdownValue))
        {
            return Formats.Markdown(markdownValue);
        }
        if (JsonSerializer.TryDeserialize<Summary>(root, options, out var summaryValue))
        {
            return Formats.Summary(summaryValue);
        }
        if (JsonSerializer.TryDeserialize<Html>(root, options, out var htmlValue))
        {
            return Formats.Html(htmlValue);
        }
        if (JsonSerializer.TryDeserialize<RawHtml>(root, options, out var rawHtmlValue))
        {
            return Formats.RawHtml(rawHtmlValue);
        }
        if (JsonSerializer.TryDeserialize<Links>(root, options, out var linksValue))
        {
            return Formats.Links(linksValue);
        }
        if (JsonSerializer.TryDeserialize<Images>(root, options, out var imagesValue))
        {
            return Formats.Images(imagesValue);
        }
        if (JsonSerializer.TryDeserialize<Screenshot>(root, options, out var screenshotValue))
        {
            return Formats.Screenshot(screenshotValue);
        }
        if (JsonSerializer.TryDeserialize<Json>(root, options, out var jsonValue))
        {
            return Formats.Json(jsonValue);
        }
        if (JsonSerializer.TryDeserialize<ChangeTracking>(root, options, out var changeTrackingValue))
        {
            return Formats.ChangeTracking(changeTrackingValue);
        }
        if (JsonSerializer.TryDeserialize<Branding>(root, options, out var brandingValue))
        {
            return Formats.Branding(brandingValue);
        }
        if (JsonSerializer.TryDeserialize<Product>(root, options, out var productValue))
        {
            return Formats.Product(productValue);
        }
        if (JsonSerializer.TryDeserialize<Menu>(root, options, out var menuValue))
        {
            return Formats.Menu(menuValue);
        }
        if (JsonSerializer.TryDeserialize<Audio>(root, options, out var audioValue))
        {
            return Formats.Audio(audioValue);
        }
        if (JsonSerializer.TryDeserialize<Video>(root, options, out var videoValue))
        {
            return Formats.Video(videoValue);
        }
        if (JsonSerializer.TryDeserialize<Question>(root, options, out var questionValue))
        {
            return Formats.Question(questionValue);
        }
        if (JsonSerializer.TryDeserialize<Highlights>(root, options, out var highlightsValue))
        {
            return Formats.Highlights(highlightsValue);
        }
        throw new JsonException($"JSON does not match Markdown or Summary or Html or RawHtml or Links or Images or Screenshot or Json or ChangeTracking or Branding or Product or Menu or Audio or Video or Question or Highlights schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, Formats value, JsonSerializerOptions options)
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
        else if (value.TryGetScreenshot(out var screenshotValue))
        {
            JsonSerializer.Serialize(writer, screenshotValue, options);
        }
        else if (value.TryGetJson(out var jsonValue))
        {
            JsonSerializer.Serialize(writer, jsonValue, options);
        }
        else if (value.TryGetChangeTracking(out var changeTrackingValue))
        {
            JsonSerializer.Serialize(writer, changeTrackingValue, options);
        }
        else if (value.TryGetBranding(out var brandingValue))
        {
            JsonSerializer.Serialize(writer, brandingValue, options);
        }
        else if (value.TryGetProduct(out var productValue))
        {
            JsonSerializer.Serialize(writer, productValue, options);
        }
        else if (value.TryGetMenu(out var menuValue))
        {
            JsonSerializer.Serialize(writer, menuValue, options);
        }
        else if (value.TryGetAudio(out var audioValue))
        {
            JsonSerializer.Serialize(writer, audioValue, options);
        }
        else if (value.TryGetVideo(out var videoValue))
        {
            JsonSerializer.Serialize(writer, videoValue, options);
        }
        else if (value.TryGetQuestion(out var questionValue))
        {
            JsonSerializer.Serialize(writer, questionValue, options);
        }
        else if (value.TryGetHighlights(out var highlightsValue))
        {
            JsonSerializer.Serialize(writer, highlightsValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(Formats)} contains no valid value to serialize.");
        }
    }
}
