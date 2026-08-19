using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Recency filter — only consider results published within this window.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SearchWindow>))]
public sealed record SearchWindow : StringEnum<SearchWindow>
{
    private SearchWindow(string value) : base(value)
    {
    }

    public static readonly SearchWindow _5M = new("5m");

    public static readonly SearchWindow _15M = new("15m");

    public static readonly SearchWindow _1H = new("1h");

    public static readonly SearchWindow _6H = new("6h");

    public static readonly SearchWindow _24H = new("24h");

    public static readonly SearchWindow _7D = new("7d");

    public static SearchWindow FromValue(string value) => FromValueCore(value);
}
