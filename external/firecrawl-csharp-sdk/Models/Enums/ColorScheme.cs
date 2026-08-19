using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// The detected color scheme of the page.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ColorScheme>))]
public sealed record ColorScheme : StringEnum<ColorScheme>
{
    private ColorScheme(string value) : base(value)
    {
    }

    public static readonly ColorScheme Light = new("light");

    public static readonly ColorScheme Dark = new("dark");

    public static ColorScheme FromValue(string value) => FromValueCore(value);
}
