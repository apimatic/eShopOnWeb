using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The verification method used. One of: <see href="https://www.twilio.com/docs/verify/email"><c>email</c></see>, <c>sms</c>, <c>whatsapp</c>, <c>call</c>, <c>sna</c>, or <c>rcs</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VerificationEnumChannel>))]
public sealed record VerificationEnumChannel : StringEnum<VerificationEnumChannel>
{
    private VerificationEnumChannel(string value) : base(value)
    {
    }

    public static readonly VerificationEnumChannel Sms = new("sms");

    public static readonly VerificationEnumChannel Call = new("call");

    public static readonly VerificationEnumChannel Email = new("email");

    public static readonly VerificationEnumChannel Whatsapp = new("whatsapp");

    public static readonly VerificationEnumChannel Sna = new("sna");

    public static VerificationEnumChannel FromValue(string value) => FromValueCore(value);
}
