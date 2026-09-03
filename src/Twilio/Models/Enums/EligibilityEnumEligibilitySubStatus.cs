using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<EligibilityEnumEligibilitySubStatus>))]
public sealed record EligibilityEnumEligibilitySubStatus : StringEnum<EligibilityEnumEligibilitySubStatus>
{
    private EligibilityEnumEligibilitySubStatus(string value) : base(value)
    {
    }

    public static readonly EligibilityEnumEligibilitySubStatus CountryIneligible = new("country-ineligible");

    public static readonly EligibilityEnumEligibilitySubStatus NumberFormatIneligible = new("number-format-ineligible");

    public static readonly EligibilityEnumEligibilitySubStatus NumberTypeIneligible = new("number-type-ineligible");

    public static readonly EligibilityEnumEligibilitySubStatus CarrierIneligible = new("carrier-ineligible");

    public static readonly EligibilityEnumEligibilitySubStatus AlreadyInTwilio = new("already-in-twilio");

    public static readonly EligibilityEnumEligibilitySubStatus InternalProcessingError = new("internal-processing-error");

    public static readonly EligibilityEnumEligibilitySubStatus InvalidPhoneNumber = new("invalid-phone-number");

    public static readonly EligibilityEnumEligibilitySubStatus InvalidHostingAccountSid = new("invalid-hosting-account-sid");

    public static readonly EligibilityEnumEligibilitySubStatus Eligible = new("eligible");

    public static EligibilityEnumEligibilitySubStatus FromValue(string value) => FromValueCore(value);
}
