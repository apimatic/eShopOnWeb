using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method that we should use to call <c>sms_fallback_url</c>. Can be: <c>GET</c> or <c>POST</c> and defaults to <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SmsFallbackMethod9>))]
public sealed record SmsFallbackMethod9 : StringEnum<SmsFallbackMethod9>
{
    private SmsFallbackMethod9(string value) : base(value)
    {
    }

    public static readonly SmsFallbackMethod9 Get = new("GET");

    public static readonly SmsFallbackMethod9 Post = new("POST");

    public static SmsFallbackMethod9 FromValue(string value) => FromValueCore(value);
}
