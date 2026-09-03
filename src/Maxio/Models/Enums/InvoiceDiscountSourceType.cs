using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<InvoiceDiscountSourceType>))]
public sealed record InvoiceDiscountSourceType : StringEnum<InvoiceDiscountSourceType>
{
    private InvoiceDiscountSourceType(string value) : base(value)
    {
    }

    public static readonly InvoiceDiscountSourceType Coupon = new("Coupon");

    public static readonly InvoiceDiscountSourceType Referral = new("Referral");

    public static readonly InvoiceDiscountSourceType AdHocCoupon = new("Ad Hoc Coupon");

    public static InvoiceDiscountSourceType FromValue(string value) => FromValueCore(value);
}
