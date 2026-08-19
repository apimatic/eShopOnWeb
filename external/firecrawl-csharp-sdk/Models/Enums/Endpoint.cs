using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Endpoint>))]
public sealed record Endpoint : StringEnum<Endpoint>
{
    private Endpoint(string value) : base(value)
    {
    }

    public static readonly Endpoint Search = new("search");

    public static readonly Endpoint Scrape = new("scrape");

    public static readonly Endpoint Parse = new("parse");

    public static readonly Endpoint Map = new("map");

    public static Endpoint FromValue(string value) => FromValueCore(value);
}
