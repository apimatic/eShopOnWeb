using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// The type of payment to be applied to an Invoice. Defaults to external.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<InvoicePaymentType>))]
public sealed record InvoicePaymentType : StringEnum<InvoicePaymentType>
{
    private InvoicePaymentType(string value) : base(value)
    {
    }

    public static readonly InvoicePaymentType External = new("external");

    public static readonly InvoicePaymentType Prepayment = new("prepayment");

    public static readonly InvoicePaymentType ServiceCredit = new("service_credit");

    public static readonly InvoicePaymentType Payment = new("payment");

    public static InvoicePaymentType FromValue(string value) => FromValueCore(value);
}
