using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Extensions;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Models.AnyOf;

[JsonConverter(typeof(WaitConverter))]
public record Wait
{
    private readonly Optional<WaitByDuration> _waitByDurationValue;

    private readonly Optional<WaitForElement> _waitForElementValue;

    private Wait(Optional<WaitByDuration> waitByDurationValue, Optional<WaitForElement> waitForElementValue)
    {
        _waitByDurationValue = waitByDurationValue;
        _waitForElementValue = waitForElementValue;
    }

    public static Wait WaitByDuration(WaitByDuration value) =>
        new(Optional<WaitByDuration>.Some(value), default);

    public static Wait WaitForElement(WaitForElement value) =>
        new(default, Optional<WaitForElement>.Some(value));

    public bool TryGetWaitByDuration(out WaitByDuration value) => _waitByDurationValue.TryGetValue(out value);

    public bool TryGetWaitForElement(out WaitForElement value) => _waitForElementValue.TryGetValue(out value);

    public static implicit operator Wait(WaitByDuration value) => WaitByDuration(value);

    public static implicit operator Wait(WaitForElement value) => WaitForElement(value);
}

file sealed class WaitConverter : JsonConverter<Wait>
{
    public override Wait Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<WaitByDuration>(root, options, out var waitByDurationValue))
        {
            return Wait.WaitByDuration(waitByDurationValue);
        }
        if (JsonSerializer.TryDeserialize<WaitForElement>(root, options, out var waitForElementValue))
        {
            return Wait.WaitForElement(waitForElementValue);
        }
        throw new JsonException($"JSON does not match WaitByDuration or WaitForElement schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, Wait value, JsonSerializerOptions options)
    {
        if (value.TryGetWaitByDuration(out var waitByDurationValue))
        {
            JsonSerializer.Serialize(writer, waitByDurationValue, options);
        }
        else if (value.TryGetWaitForElement(out var waitForElementValue))
        {
            JsonSerializer.Serialize(writer, waitForElementValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(Wait)} contains no valid value to serialize.");
        }
    }
}
