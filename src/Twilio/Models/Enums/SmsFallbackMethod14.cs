using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method that we should use to call the <c>sms_fallback_url</c>. Can be: <c>GET</c> or <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SmsFallbackMethod14>))]
public sealed record SmsFallbackMethod14 : StringEnum<SmsFallbackMethod14>
{
    private SmsFallbackMethod14(string value) : base(value)
    {
    }

    public static readonly SmsFallbackMethod14 Get = new("GET");

    public static readonly SmsFallbackMethod14 Post = new("POST");

    public static SmsFallbackMethod14 FromValue(string value) => FromValueCore(value);
}
