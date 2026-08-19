using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Doc>))]
public sealed record Doc : StringEnum<Doc>
{
    private Doc(string value) : base(value)
    {
    }

    public static readonly Doc Ok = new("ok");

    public static readonly Doc Degraded = new("degraded");

    public static readonly Doc Unavailable = new("unavailable");

    public static readonly Doc Skipped = new("skipped");

    public static Doc FromValue(string value) => FromValueCore(value);
}
