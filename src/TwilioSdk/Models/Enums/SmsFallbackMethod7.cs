using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method we should use to call <c>sms_fallback_url</c>. Can be: <c>GET</c> or <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SmsFallbackMethod7>))]
public sealed record SmsFallbackMethod7 : StringEnum<SmsFallbackMethod7>
{
    private SmsFallbackMethod7(string value) : base(value)
    {
    }

    public static readonly SmsFallbackMethod7 Get = new("GET");

    public static readonly SmsFallbackMethod7 Post = new("POST");

    public static SmsFallbackMethod7 FromValue(string value) => FromValueCore(value);
}
