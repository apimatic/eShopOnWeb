using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Proxy mode for parse uploads. <c>/parse</c> supports only <c>basic</c> and <c>auto</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Proxy1>))]
public sealed record Proxy1 : StringEnum<Proxy1>
{
    private Proxy1(string value) : base(value)
    {
    }

    public static readonly Proxy1 Basic = new("basic");

    public static readonly Proxy1 Auto = new("auto");

    public static Proxy1 FromValue(string value) => FromValueCore(value);
}
