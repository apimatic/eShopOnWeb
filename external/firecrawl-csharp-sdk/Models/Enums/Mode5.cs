using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Mode5>))]
public sealed record Mode5 : StringEnum<Mode5>
{
    private Mode5(string value) : base(value)
    {
    }

    public static readonly Mode5 Similar = new("similar");

    public static readonly Mode5 Citers = new("citers");

    public static readonly Mode5 References = new("references");

    public static Mode5 FromValue(string value) => FromValueCore(value);
}
