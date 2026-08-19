using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// URL scanning mode for this request. <c>normal</c> checks URLs against Google Web Risk (+2 credits per URL scanned).
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Mode3>))]
public sealed record Mode3 : StringEnum<Mode3>
{
    private Mode3(string value) : base(value)
    {
    }

    public static readonly Mode3 Off = new("off");

    public static readonly Mode3 Normal = new("normal");

    public static Mode3 FromValue(string value) => FromValueCore(value);
}
