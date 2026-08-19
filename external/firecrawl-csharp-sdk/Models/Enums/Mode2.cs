using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Redaction strategy. <c>accurate</c> is model-only and optimized for precision, <c>aggressive</c> increases recall with additional heuristics, and <c>fast</c> uses heuristics without the model call.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Mode2>))]
public sealed record Mode2 : StringEnum<Mode2>
{
    private Mode2(string value) : base(value)
    {
    }

    public static readonly Mode2 Accurate = new("accurate");

    public static readonly Mode2 Aggressive = new("aggressive");

    public static readonly Mode2 Fast = new("fast");

    public static Mode2 FromValue(string value) => FromValueCore(value);
}
