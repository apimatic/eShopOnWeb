using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Confidence>))]
public sealed record Confidence : StringEnum<Confidence>
{
    private Confidence(string value) : base(value)
    {
    }

    public static readonly Confidence High = new("high");

    public static readonly Confidence Medium = new("medium");

    public static readonly Confidence Low = new("low");

    public static Confidence FromValue(string value) => FromValueCore(value);
}
