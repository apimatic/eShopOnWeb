using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<VerificationAttemptsSummaryEnumChannels>))]
public sealed record VerificationAttemptsSummaryEnumChannels : StringEnum<VerificationAttemptsSummaryEnumChannels>
{
    private VerificationAttemptsSummaryEnumChannels(string value) : base(value)
    {
    }

    public static readonly VerificationAttemptsSummaryEnumChannels Sms = new("sms");

    public static readonly VerificationAttemptsSummaryEnumChannels Call = new("call");

    public static readonly VerificationAttemptsSummaryEnumChannels Email = new("email");

    public static readonly VerificationAttemptsSummaryEnumChannels Whatsapp = new("whatsapp");

    public static readonly VerificationAttemptsSummaryEnumChannels Rbm = new("rbm");

    public static VerificationAttemptsSummaryEnumChannels FromValue(string value) => FromValueCore(value);
}
