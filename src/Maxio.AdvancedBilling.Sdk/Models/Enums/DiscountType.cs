using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<DiscountType>))]
public sealed record DiscountType : StringEnum<DiscountType>
{
    private DiscountType(string value) : base(value)
    {
    }

    public static readonly DiscountType Amount = new("amount");

    public static readonly DiscountType Percent = new("percent");

    public static DiscountType FromValue(string value) => FromValueCore(value);
}
