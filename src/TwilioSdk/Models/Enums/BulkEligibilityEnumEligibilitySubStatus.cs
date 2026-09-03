using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<BulkEligibilityEnumEligibilitySubStatus>))]
public sealed record BulkEligibilityEnumEligibilitySubStatus : StringEnum<BulkEligibilityEnumEligibilitySubStatus>
{
    private BulkEligibilityEnumEligibilitySubStatus(string value) : base(value)
    {
    }

    public static readonly BulkEligibilityEnumEligibilitySubStatus CountryIneligible = new("country-ineligible");

    public static readonly BulkEligibilityEnumEligibilitySubStatus NumberFormatIneligible = new("number-format-ineligible");

    public static readonly BulkEligibilityEnumEligibilitySubStatus NumberTypeIneligible = new("number-type-ineligible");

    public static readonly BulkEligibilityEnumEligibilitySubStatus CarrierIneligible = new("carrier-ineligible");

    public static readonly BulkEligibilityEnumEligibilitySubStatus AlreadyInTwilio = new("already-in-twilio");

    public static readonly BulkEligibilityEnumEligibilitySubStatus InternalProcessingError = new("internal-processing-error");

    public static readonly BulkEligibilityEnumEligibilitySubStatus InvalidPhoneNumber = new("invalid-phone-number");

    public static readonly BulkEligibilityEnumEligibilitySubStatus InvalidHostingAccountSid = new("invalid-hosting-account-sid");

    public static readonly BulkEligibilityEnumEligibilitySubStatus Eligible = new("eligible");

    public static readonly BulkEligibilityEnumEligibilitySubStatus EligibleByManualProcess = new("eligible-by-manual-process");

    public static BulkEligibilityEnumEligibilitySubStatus FromValue(string value) => FromValueCore(value);
}
