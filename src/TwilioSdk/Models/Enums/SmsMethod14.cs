using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method we should use when calling the <c>sms_url</c>. Can be: <c>GET</c> or <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SmsMethod14>))]
public sealed record SmsMethod14 : StringEnum<SmsMethod14>
{
    private SmsMethod14(string value) : base(value)
    {
    }

    public static readonly SmsMethod14 Get = new("GET");

    public static readonly SmsMethod14 Post = new("POST");

    public static SmsMethod14 FromValue(string value) => FromValueCore(value);
}
