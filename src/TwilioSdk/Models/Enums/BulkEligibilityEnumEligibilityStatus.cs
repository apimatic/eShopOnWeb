using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

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
