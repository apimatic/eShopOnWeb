using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<EligibilityEnumEligibilityStatus>))]
public sealed record EligibilityEnumEligibilityStatus : StringEnum<EligibilityEnumEligibilityStatus>
{
    private EligibilityEnumEligibilityStatus(string value) : base(value)
    {
    }

    public static readonly EligibilityEnumEligibilityStatus Ineligible = new("ineligible");

    public static readonly EligibilityEnumEligibilityStatus Eligible = new("eligible");

    public static EligibilityEnumEligibilityStatus FromValue(string value) => FromValueCore(value);
}
