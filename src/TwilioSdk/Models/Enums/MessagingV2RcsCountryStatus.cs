using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The country-level status. Based on the aggregation of the carrier-level status.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<MessagingV2RcsCountryStatus>))]
public sealed record MessagingV2RcsCountryStatus : StringEnum<MessagingV2RcsCountryStatus>
{
    private MessagingV2RcsCountryStatus(string value) : base(value)
    {
    }

    public static readonly MessagingV2RcsCountryStatus Online = new("ONLINE");

    public static readonly MessagingV2RcsCountryStatus Offline = new("OFFLINE");

    public static readonly MessagingV2RcsCountryStatus TwilioReview = new("TWILIO_REVIEW");

    public static readonly MessagingV2RcsCountryStatus PendingVerification = new("PENDING_VERIFICATION");

    public static MessagingV2RcsCountryStatus FromValue(string value) => FromValueCore(value);
}
