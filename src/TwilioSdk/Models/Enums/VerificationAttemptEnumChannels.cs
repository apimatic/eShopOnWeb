using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// A string specifying the communication channel used for the verification attempt.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VerificationAttemptEnumChannels>))]
public sealed record VerificationAttemptEnumChannels : StringEnum<VerificationAttemptEnumChannels>
{
    private VerificationAttemptEnumChannels(string value) : base(value)
    {
    }

    public static readonly VerificationAttemptEnumChannels Sms = new("sms");

    public static readonly VerificationAttemptEnumChannels Call = new("call");

    public static readonly VerificationAttemptEnumChannels Email = new("email");

    public static readonly VerificationAttemptEnumChannels Whatsapp = new("whatsapp");

    public static readonly VerificationAttemptEnumChannels Rbm = new("rbm");

    public static VerificationAttemptEnumChannels FromValue(string value) => FromValueCore(value);
}
