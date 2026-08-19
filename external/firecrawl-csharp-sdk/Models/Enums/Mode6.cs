using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Threat protection mode. <c>off</c> disables checks; <c>normal</c> checks URLs against Google Web Risk (+2 credits per URL scanned).
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Mode6>))]
public sealed record Mode6 : StringEnum<Mode6>
{
    private Mode6(string value) : base(value)
    {
    }

    public static readonly Mode6 Off = new("off");

    public static readonly Mode6 Normal = new("normal");

    public static Mode6 FromValue(string value) => FromValueCore(value);
}
