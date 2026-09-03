using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method we use to call the <c>sms_fallback_url</c>. Can be: <c>GET</c> or <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SmsFallbackMethod6>))]
public sealed record SmsFallbackMethod6 : StringEnum<SmsFallbackMethod6>
{
    private SmsFallbackMethod6(string value) : base(value)
    {
    }

    public static readonly SmsFallbackMethod6 Get = new("GET");

    public static readonly SmsFallbackMethod6 Post = new("POST");

    public static SmsFallbackMethod6 FromValue(string value) => FromValueCore(value);
}
