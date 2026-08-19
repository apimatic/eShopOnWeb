using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Sitemap mode when mapping. If you set it to <c>skip</c>, the sitemap won't be used to find URLs. If you set it to <c>only</c>, only URLs that are in the sitemap will be returned. By default (<c>include</c>), the sitemap and other methods will be used together to find URLs.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Sitemap2>))]
public sealed record Sitemap2 : StringEnum<Sitemap2>
{
    private Sitemap2(string value) : base(value)
    {
    }

    public static readonly Sitemap2 Skip = new("skip");

    public static readonly Sitemap2 Include = new("include");

    public static readonly Sitemap2 Only = new("only");

    public static Sitemap2 FromValue(string value) => FromValueCore(value);
}
