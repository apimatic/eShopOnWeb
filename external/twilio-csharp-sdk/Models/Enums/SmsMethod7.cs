using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method we should use to call <c>sms_url</c>. Can be: <c>GET</c> or <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SmsMethod7>))]
public sealed record SmsMethod7 : StringEnum<SmsMethod7>
{
    private SmsMethod7(string value) : base(value)
    {
    }

    public static readonly SmsMethod7 Get = new("GET");

    public static readonly SmsMethod7 Post = new("POST");

    public static SmsMethod7 FromValue(string value) => FromValueCore(value);
}
