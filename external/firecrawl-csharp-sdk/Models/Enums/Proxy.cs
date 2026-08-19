using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Specifies the type of proxy to use.
/// <list type="bullet">
///   <item><description><b>basic</b>: Proxies for scraping sites with none to basic anti-bot solutions. Fast and usually works.</description></item>
///   <item><description><b>enhanced</b>: Enhanced proxies for scraping sites with advanced anti-bot solutions. Slower, but more reliable on certain sites. Billed at the same credit cost as basic.</description></item>
///   <item><description><b>auto</b>: Firecrawl will automatically retry scraping with enhanced proxies if the basic proxy fails. Enhanced proxies carry no credit surcharge, so either way only the regular cost is billed.</description></item>
/// </list>
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Proxy>))]
public sealed record Proxy : StringEnum<Proxy>
{
    private Proxy(string value) : base(value)
    {
    }

    public static readonly Proxy Basic = new("basic");

    public static readonly Proxy Enhanced = new("enhanced");

    public static readonly Proxy Auto = new("auto");

    public static Proxy FromValue(string value) => FromValueCore(value);
}
