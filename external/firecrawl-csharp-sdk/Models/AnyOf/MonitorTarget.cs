using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Extensions;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Models.AnyOf;

[JsonConverter(typeof(MonitorTargetConverter))]
public record MonitorTarget
{
    private readonly Optional<ScrapeTarget> _scrapeTargetValue;

    private readonly Optional<CrawlTarget> _crawlTargetValue;

    private readonly Optional<SearchTarget> _searchTargetValue;

    private MonitorTarget(Optional<ScrapeTarget> scrapeTargetValue,
        Optional<CrawlTarget> crawlTargetValue,
        Optional<SearchTarget> searchTargetValue)
    {
        _scrapeTargetValue = scrapeTargetValue;
        _crawlTargetValue = crawlTargetValue;
        _searchTargetValue = searchTargetValue;
    }

    public static MonitorTarget ScrapeTarget(ScrapeTarget value) =>
        new(Optional<ScrapeTarget>.Some(value), default, default);

    public static MonitorTarget CrawlTarget(CrawlTarget value) =>
        new(default, Optional<CrawlTarget>.Some(value), default);

    public static MonitorTarget SearchTarget(SearchTarget value) =>
        new(default, default, Optional<SearchTarget>.Some(value));

    public bool TryGetScrapeTarget(out ScrapeTarget value) => _scrapeTargetValue.TryGetValue(out value);

    public bool TryGetCrawlTarget(out CrawlTarget value) => _crawlTargetValue.TryGetValue(out value);

    public bool TryGetSearchTarget(out SearchTarget value) => _searchTargetValue.TryGetValue(out value);

    public static implicit operator MonitorTarget(ScrapeTarget value) => ScrapeTarget(value);

    public static implicit operator MonitorTarget(CrawlTarget value) => CrawlTarget(value);

    public static implicit operator MonitorTarget(SearchTarget value) => SearchTarget(value);
}

file sealed class MonitorTargetConverter : JsonConverter<MonitorTarget>
{
    public override MonitorTarget Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<ScrapeTarget>(root, options, out var scrapeTargetValue))
        {
            return MonitorTarget.ScrapeTarget(scrapeTargetValue);
        }
        if (JsonSerializer.TryDeserialize<CrawlTarget>(root, options, out var crawlTargetValue))
        {
            return MonitorTarget.CrawlTarget(crawlTargetValue);
        }
        if (JsonSerializer.TryDeserialize<SearchTarget>(root, options, out var searchTargetValue))
        {
            return MonitorTarget.SearchTarget(searchTargetValue);
        }
        throw new JsonException($"JSON does not match ScrapeTarget or CrawlTarget or SearchTarget schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, MonitorTarget value, JsonSerializerOptions options)
    {
        if (value.TryGetScrapeTarget(out var scrapeTargetValue))
        {
            JsonSerializer.Serialize(writer, scrapeTargetValue, options);
        }
        else if (value.TryGetCrawlTarget(out var crawlTargetValue))
        {
            JsonSerializer.Serialize(writer, crawlTargetValue, options);
        }
        else if (value.TryGetSearchTarget(out var searchTargetValue))
        {
            JsonSerializer.Serialize(writer, searchTargetValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(MonitorTarget)} contains no valid value to serialize.");
        }
    }
}
