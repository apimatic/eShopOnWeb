using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<InvoiceDiscountType>))]
public sealed record InvoiceDiscountType : StringEnum<InvoiceDiscountType>
{
    private InvoiceDiscountType(string value) : base(value)
    {
    }

    public static readonly InvoiceDiscountType Percentage = new("percentage");

    public static readonly InvoiceDiscountType FlatAmount = new("flat_amount");

    public static readonly InvoiceDiscountType Rollover = new("rollover");

    public static InvoiceDiscountType FromValue(string value) => FromValueCore(value);
}
