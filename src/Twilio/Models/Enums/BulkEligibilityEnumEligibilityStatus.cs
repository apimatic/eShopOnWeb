using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<BulkEligibilityEnumEligibilityStatus>))]
public sealed record BulkEligibilityEnumEligibilityStatus : StringEnum<BulkEligibilityEnumEligibilityStatus>
{
    private BulkEligibilityEnumEligibilityStatus(string value) : base(value)
    {
    }

    public static readonly BulkEligibilityEnumEligibilityStatus Ineligible = new("ineligible");

    public static readonly BulkEligibilityEnumEligibilityStatus Eligible = new("eligible");

    public static BulkEligibilityEnumEligibilityStatus FromValue(string value) => FromValueCore(value);
}
