using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The verification method to use. One of: <see href="https://www.twilio.com/docs/verify/email"><c>email</c></see>, <c>sms</c>, <c>whatsapp</c>, <c>call</c>, or <c>sna</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VerificationCheckEnumChannel>))]
public sealed record VerificationCheckEnumChannel : StringEnum<VerificationCheckEnumChannel>
{
    private VerificationCheckEnumChannel(string value) : base(value)
    {
    }

    public static readonly VerificationCheckEnumChannel Sms = new("sms");

    public static readonly VerificationCheckEnumChannel Call = new("call");

    public static readonly VerificationCheckEnumChannel Email = new("email");

    public static readonly VerificationCheckEnumChannel Whatsapp = new("whatsapp");

    public static readonly VerificationCheckEnumChannel Sna = new("sna");

    public static VerificationCheckEnumChannel FromValue(string value) => FromValueCore(value);
}
