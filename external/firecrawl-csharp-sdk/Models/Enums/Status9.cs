using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Status9>))]
public sealed record Status9 : StringEnum<Status9>
{
    private Status9(string value) : base(value)
    {
    }

    public static readonly Status9 Processing = new("processing");

    public static readonly Status9 Completed = new("completed");

    public static readonly Status9 Failed = new("failed");

    public static Status9 FromValue(string value) => FromValueCore(value);
}
