using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// The visibility of the current page/URL. 'visible' means the URL was discovered through an organic route (links or sitemap), 'hidden' means the URL was discovered through memory from previous crawls.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Visibility>))]
public sealed record Visibility : StringEnum<Visibility>
{
    private Visibility(string value) : base(value)
    {
    }

    public static readonly Visibility Visible = new("visible");

    public static readonly Visibility Hidden = new("hidden");

    public static Visibility FromValue(string value) => FromValueCore(value);
}
