using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<FirstChargeType>))]
public sealed record FirstChargeType : StringEnum<FirstChargeType>
{
    private FirstChargeType(string value) : base(value)
    {
    }

    public static readonly FirstChargeType Prorated = new("prorated");

    public static readonly FirstChargeType Immediate = new("immediate");

    public static readonly FirstChargeType Delayed = new("delayed");

    public static FirstChargeType FromValue(string value) => FromValueCore(value);
}
