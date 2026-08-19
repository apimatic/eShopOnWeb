using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Mode>))]
public sealed record Mode : StringEnum<Mode>
{
    private Mode(string value) : base(value)
    {
    }

    public static readonly Mode GitDiff = new("git-diff");

    public static readonly Mode Json = new("json");

    public static Mode FromValue(string value) => FromValueCore(value);
}
