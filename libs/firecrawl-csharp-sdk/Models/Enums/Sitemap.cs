using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Sitemap mode when crawling. If you set it to 'skip', the crawler will ignore the website sitemap and only crawl the entered URL and discover pages from there onwards. If you set it to 'only', the crawler will only crawl URLs from the sitemap (plus the start URL) and will not discover links from HTML.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Sitemap>))]
public sealed record Sitemap : StringEnum<Sitemap>
{
    private Sitemap(string value) : base(value)
    {
    }

    public static readonly Sitemap Skip = new("skip");

    public static readonly Sitemap Include = new("include");

    public static readonly Sitemap Only = new("only");

    public static Sitemap FromValue(string value) => FromValueCore(value);
}
