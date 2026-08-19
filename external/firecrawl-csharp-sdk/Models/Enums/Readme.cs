using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Readme>))]
public sealed record Readme : StringEnum<Readme>
{
    private Readme(string value) : base(value)
    {
    }

    public static readonly Readme Ok = new("ok");

    public static readonly Readme Degraded = new("degraded");

    public static readonly Readme Unavailable = new("unavailable");

    public static readonly Readme Skipped = new("skipped");

    public static Readme FromValue(string value) => FromValueCore(value);
}
