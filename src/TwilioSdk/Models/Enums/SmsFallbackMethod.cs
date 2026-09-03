using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method we use to call <c>sms_fallback_url</c>. Can be: <c>GET</c> or <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SmsFallbackMethod>))]
public sealed record SmsFallbackMethod : StringEnum<SmsFallbackMethod>
{
    private SmsFallbackMethod(string value) : base(value)
    {
    }

    public static readonly SmsFallbackMethod Get = new("GET");

    public static readonly SmsFallbackMethod Post = new("POST");

    public static SmsFallbackMethod FromValue(string value) => FromValueCore(value);
}
