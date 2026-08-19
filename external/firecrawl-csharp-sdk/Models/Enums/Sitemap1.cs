using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Sitemap handling strategy
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Sitemap1>))]
public sealed record Sitemap1 : StringEnum<Sitemap1>
{
    private Sitemap1(string value) : base(value)
    {
    }

    public static readonly Sitemap1 Skip = new("skip");

    public static readonly Sitemap1 Include = new("include");

    public static Sitemap1 FromValue(string value) => FromValueCore(value);
}
