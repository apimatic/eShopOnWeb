using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ProformaInvoiceDiscountSourceType>))]
public sealed record ProformaInvoiceDiscountSourceType : StringEnum<ProformaInvoiceDiscountSourceType>
{
    private ProformaInvoiceDiscountSourceType(string value) : base(value)
    {
    }

    public static readonly ProformaInvoiceDiscountSourceType Coupon = new("Coupon");

    public static readonly ProformaInvoiceDiscountSourceType Referral = new("Referral");

    public static ProformaInvoiceDiscountSourceType FromValue(string value) => FromValueCore(value);
}
