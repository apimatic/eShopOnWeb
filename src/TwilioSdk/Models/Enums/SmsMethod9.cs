using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method that we should use to call <c>sms_url</c>. Can be: <c>GET</c> or <c>POST</c> and defaults to <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SmsMethod9>))]
public sealed record SmsMethod9 : StringEnum<SmsMethod9>
{
    private SmsMethod9(string value) : base(value)
    {
    }

    public static readonly SmsMethod9 Get = new("GET");

    public static readonly SmsMethod9 Post = new("POST");

    public static SmsMethod9 FromValue(string value) => FromValueCore(value);
}
