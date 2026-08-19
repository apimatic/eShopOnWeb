using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Issue>))]
public sealed record Issue : StringEnum<Issue>
{
    private Issue(string value) : base(value)
    {
    }

    public static readonly Issue Ok = new("ok");

    public static readonly Issue Degraded = new("degraded");

    public static readonly Issue Unavailable = new("unavailable");

    public static readonly Issue Skipped = new("skipped");

    public static Issue FromValue(string value) => FromValueCore(value);
}
