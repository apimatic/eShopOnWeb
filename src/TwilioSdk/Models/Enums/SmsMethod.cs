using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method we use to call <c>sms_url</c>. Can be: <c>GET</c> or <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SmsMethod>))]
public sealed record SmsMethod : StringEnum<SmsMethod>
{
    private SmsMethod(string value) : base(value)
    {
    }

    public static readonly SmsMethod Get = new("GET");

    public static readonly SmsMethod Post = new("POST");

    public static SmsMethod FromValue(string value) => FromValueCore(value);
}
