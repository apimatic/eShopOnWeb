using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The description of the rejection reason provided by the losing carrier. This field may be null if the number has not been rejected by the losing carrier.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RejectionReason>))]
public sealed record RejectionReason : StringEnum<RejectionReason>
{
    private RejectionReason(string value) : base(value)
    {
    }

    public static readonly RejectionReason ContactSupportRequired = new("CONTACT_SUPPORT_REQUIRED");

    public static readonly RejectionReason PhoneNumberWithCarrierRestriction = new("PHONE_NUMBER_WITH_CARRIER_RESTRICTION");

    public static readonly RejectionReason PhoneNumberInactiveOrDisconnected = new("PHONE_NUMBER_INACTIVE_OR_DISCONNECTED");

    public static readonly RejectionReason InvalidEndUserName = new("INVALID_END_USER_NAME");

    public static readonly RejectionReason InvalidAddress = new("INVALID_ADDRESS");

    public static readonly RejectionReason InvalidPin = new("INVALID_PIN");

    public static readonly RejectionReason InvalidAccountNumber = new("INVALID_ACCOUNT_NUMBER");

    public static RejectionReason FromValue(string value) => FromValueCore(value);
}
