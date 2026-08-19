using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// The endpoint used for this job
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Endpoint2>))]
public sealed record Endpoint2 : StringEnum<Endpoint2>
{
    private Endpoint2(string value) : base(value)
    {
    }

    public static readonly Endpoint2 Scrape = new("scrape");

    public static readonly Endpoint2 Crawl = new("crawl");

    public static readonly Endpoint2 BatchScrape = new("batch_scrape");

    public static readonly Endpoint2 Search = new("search");

    public static readonly Endpoint2 Extract = new("extract");

    public static readonly Endpoint2 Llmstxt = new("llmstxt");

    public static readonly Endpoint2 DeepResearch = new("deep_research");

    public static readonly Endpoint2 Map = new("map");

    public static readonly Endpoint2 Agent = new("agent");

    public static readonly Endpoint2 Browser = new("browser");

    public static readonly Endpoint2 Interact = new("interact");

    public static Endpoint2 FromValue(string value) => FromValueCore(value);
}
