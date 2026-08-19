using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Status10>))]
public sealed record Status10 : StringEnum<Status10>
{
    private Status10(string value) : base(value)
    {
    }

    public static readonly Status10 Active = new("active");

    public static readonly Status10 Destroyed = new("destroyed");

    public static Status10 FromValue(string value) => FromValueCore(value);
}
