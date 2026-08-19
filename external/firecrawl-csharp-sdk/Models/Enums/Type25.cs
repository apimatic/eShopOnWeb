using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Scrape the current page content, returns the url and the html.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Type25>))]
public sealed record Type25 : StringEnum<Type25>
{
    private Type25(string value) : base(value)
    {
    }

    public static readonly Type25 Scrape = new("scrape");

    public static Type25 FromValue(string value) => FromValueCore(value);
}
