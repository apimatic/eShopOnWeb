using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Trigger>))]
public sealed record Trigger : StringEnum<Trigger>
{
    private Trigger(string value) : base(value)
    {
    }

    public static readonly Trigger Scheduled = new("scheduled");

    public static readonly Trigger Manual = new("manual");

    public static Trigger FromValue(string value) => FromValueCore(value);
}
