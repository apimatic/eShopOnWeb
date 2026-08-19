using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Rating>))]
public sealed record Rating : StringEnum<Rating>
{
    private Rating(string value) : base(value)
    {
    }

    public static readonly Rating Good = new("good");

    public static readonly Rating Partial = new("partial");

    public static readonly Rating Bad = new("bad");

    public static Rating FromValue(string value) => FromValueCore(value);
}
