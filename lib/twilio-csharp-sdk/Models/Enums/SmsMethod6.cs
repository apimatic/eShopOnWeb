using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method we use to call the <c>sms_url</c>. Can be: <c>GET</c> or <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SmsMethod6>))]
public sealed record SmsMethod6 : StringEnum<SmsMethod6>
{
    private SmsMethod6(string value) : base(value)
    {
    }

    public static readonly SmsMethod6 Get = new("GET");

    public static readonly SmsMethod6 Post = new("POST");

    public static SmsMethod6 FromValue(string value) => FromValueCore(value);
}
