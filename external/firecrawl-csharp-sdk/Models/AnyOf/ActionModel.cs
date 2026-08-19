using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Extensions;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Models.AnyOf;

[JsonConverter(typeof(ActionModelConverter))]
public record ActionModel
{
    private readonly Optional<Wait> _waitValue;

    private readonly Optional<Screenshot1> _screenshot1Value;

    private readonly Optional<Click> _clickValue;

    private readonly Optional<WriteText> _writeTextValue;

    private readonly Optional<PressAKey> _pressAKeyValue;

    private readonly Optional<Scroll> _scrollValue;

    private readonly Optional<Scrape> _scrapeValue;

    private readonly Optional<ExecuteJavaScript> _executeJavaScriptValue;

    private readonly Optional<GeneratePdf> _generatePdfValue;

    private ActionModel(Optional<Wait> waitValue,
        Optional<Screenshot1> screenshot1Value,
        Optional<Click> clickValue,
        Optional<WriteText> writeTextValue,
        Optional<PressAKey> pressAKeyValue,
        Optional<Scroll> scrollValue,
        Optional<Scrape> scrapeValue,
        Optional<ExecuteJavaScript> executeJavaScriptValue,
        Optional<GeneratePdf> generatePdfValue)
    {
        _waitValue = waitValue;
        _screenshot1Value = screenshot1Value;
        _clickValue = clickValue;
        _writeTextValue = writeTextValue;
        _pressAKeyValue = pressAKeyValue;
        _scrollValue = scrollValue;
        _scrapeValue = scrapeValue;
        _executeJavaScriptValue = executeJavaScriptValue;
        _generatePdfValue = generatePdfValue;
    }

    public static ActionModel Wait(Wait value) =>
        new(Optional<Wait>.Some(value), default, default, default, default, default, default, default, default);

    public static ActionModel Screenshot1(Screenshot1 value) =>
        new(default, Optional<Screenshot1>.Some(value), default, default, default, default, default, default, default);

    public static ActionModel Click(Click value) =>
        new(default, default, Optional<Click>.Some(value), default, default, default, default, default, default);

    public static ActionModel WriteText(WriteText value) =>
        new(default, default, default, Optional<WriteText>.Some(value), default, default, default, default, default);

    public static ActionModel PressAKey(PressAKey value) =>
        new(default, default, default, default, Optional<PressAKey>.Some(value), default, default, default, default);

    public static ActionModel Scroll(Scroll value) =>
        new(default, default, default, default, default, Optional<Scroll>.Some(value), default, default, default);

    public static ActionModel Scrape(Scrape value) =>
        new(default, default, default, default, default, default, Optional<Scrape>.Some(value), default, default);

    public static ActionModel ExecuteJavaScript(ExecuteJavaScript value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<ExecuteJavaScript>.Some(value),
            default);

    public static ActionModel GeneratePdf(GeneratePdf value) =>
        new(default, default, default, default, default, default, default, default, Optional<GeneratePdf>.Some(value));

    public bool TryGetWait(out Wait value) => _waitValue.TryGetValue(out value);

    public bool TryGetScreenshot1(out Screenshot1 value) => _screenshot1Value.TryGetValue(out value);

    public bool TryGetClick(out Click value) => _clickValue.TryGetValue(out value);

    public bool TryGetWriteText(out WriteText value) => _writeTextValue.TryGetValue(out value);

    public bool TryGetPressAKey(out PressAKey value) => _pressAKeyValue.TryGetValue(out value);

    public bool TryGetScroll(out Scroll value) => _scrollValue.TryGetValue(out value);

    public bool TryGetScrape(out Scrape value) => _scrapeValue.TryGetValue(out value);

    public bool TryGetExecuteJavaScript(out ExecuteJavaScript value) =>
        _executeJavaScriptValue.TryGetValue(out value);

    public bool TryGetGeneratePdf(out GeneratePdf value) => _generatePdfValue.TryGetValue(out value);

    public static implicit operator ActionModel(Wait value) => Wait(value);

    public static implicit operator ActionModel(Screenshot1 value) => Screenshot1(value);

    public static implicit operator ActionModel(Click value) => Click(value);

    public static implicit operator ActionModel(WriteText value) => WriteText(value);

    public static implicit operator ActionModel(PressAKey value) => PressAKey(value);

    public static implicit operator ActionModel(Scroll value) => Scroll(value);

    public static implicit operator ActionModel(Scrape value) => Scrape(value);

    public static implicit operator ActionModel(ExecuteJavaScript value) => ExecuteJavaScript(value);

    public static implicit operator ActionModel(GeneratePdf value) => GeneratePdf(value);
}

file sealed class ActionModelConverter : JsonConverter<ActionModel>
{
    public override ActionModel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<Wait>(root, options, out var waitValue))
        {
            return ActionModel.Wait(waitValue);
        }
        if (JsonSerializer.TryDeserialize<Screenshot1>(root, options, out var screenshot1Value))
        {
            return ActionModel.Screenshot1(screenshot1Value);
        }
        if (JsonSerializer.TryDeserialize<Click>(root, options, out var clickValue))
        {
            return ActionModel.Click(clickValue);
        }
        if (JsonSerializer.TryDeserialize<WriteText>(root, options, out var writeTextValue))
        {
            return ActionModel.WriteText(writeTextValue);
        }
        if (JsonSerializer.TryDeserialize<PressAKey>(root, options, out var pressAKeyValue))
        {
            return ActionModel.PressAKey(pressAKeyValue);
        }
        if (JsonSerializer.TryDeserialize<Scroll>(root, options, out var scrollValue))
        {
            return ActionModel.Scroll(scrollValue);
        }
        if (JsonSerializer.TryDeserialize<Scrape>(root, options, out var scrapeValue))
        {
            return ActionModel.Scrape(scrapeValue);
        }
        if (JsonSerializer.TryDeserialize<ExecuteJavaScript>(root, options, out var executeJavaScriptValue))
        {
            return ActionModel.ExecuteJavaScript(executeJavaScriptValue);
        }
        if (JsonSerializer.TryDeserialize<GeneratePdf>(root, options, out var generatePdfValue))
        {
            return ActionModel.GeneratePdf(generatePdfValue);
        }
        throw new JsonException($"JSON does not match Wait or Screenshot1 or Click or WriteText or PressAKey or Scroll or Scrape or ExecuteJavaScript or GeneratePdf schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, ActionModel value, JsonSerializerOptions options)
    {
        if (value.TryGetWait(out var waitValue))
        {
            JsonSerializer.Serialize(writer, waitValue, options);
        }
        else if (value.TryGetScreenshot1(out var screenshot1Value))
        {
            JsonSerializer.Serialize(writer, screenshot1Value, options);
        }
        else if (value.TryGetClick(out var clickValue))
        {
            JsonSerializer.Serialize(writer, clickValue, options);
        }
        else if (value.TryGetWriteText(out var writeTextValue))
        {
            JsonSerializer.Serialize(writer, writeTextValue, options);
        }
        else if (value.TryGetPressAKey(out var pressAKeyValue))
        {
            JsonSerializer.Serialize(writer, pressAKeyValue, options);
        }
        else if (value.TryGetScroll(out var scrollValue))
        {
            JsonSerializer.Serialize(writer, scrollValue, options);
        }
        else if (value.TryGetScrape(out var scrapeValue))
        {
            JsonSerializer.Serialize(writer, scrapeValue, options);
        }
        else if (value.TryGetExecuteJavaScript(out var executeJavaScriptValue))
        {
            JsonSerializer.Serialize(writer, executeJavaScriptValue, options);
        }
        else if (value.TryGetGeneratePdf(out var generatePdfValue))
        {
            JsonSerializer.Serialize(writer, generatePdfValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(ActionModel)} contains no valid value to serialize.");
        }
    }
}
