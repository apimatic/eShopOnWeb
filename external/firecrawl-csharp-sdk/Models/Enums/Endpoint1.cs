using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Endpoint1>))]
public sealed record Endpoint1 : StringEnum<Endpoint1>
{
    private Endpoint1(string value) : base(value)
    {
    }

    public static readonly Endpoint1 Scrape = new("scrape");

    public static readonly Endpoint1 Crawl = new("crawl");

    public static readonly Endpoint1 BatchScrape = new("batch_scrape");

    public static readonly Endpoint1 Search = new("search");

    public static readonly Endpoint1 Extract = new("extract");

    public static readonly Endpoint1 Llmstxt = new("llmstxt");

    public static readonly Endpoint1 DeepResearch = new("deep_research");

    public static readonly Endpoint1 Map = new("map");

    public static readonly Endpoint1 Agent = new("agent");

    public static readonly Endpoint1 Browser = new("browser");

    public static readonly Endpoint1 Interact = new("interact");

    public static Endpoint1 FromValue(string value) => FromValueCore(value);
}
