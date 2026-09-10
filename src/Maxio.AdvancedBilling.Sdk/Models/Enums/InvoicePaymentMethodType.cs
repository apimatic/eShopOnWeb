using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// The type of payment method used. Defaults to other.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<InvoicePaymentMethodType>))]
public sealed record InvoicePaymentMethodType : StringEnum<InvoicePaymentMethodType>
{
    private InvoicePaymentMethodType(string value) : base(value)
    {
    }

    public static readonly InvoicePaymentMethodType CreditCard = new("credit_card");

    public static readonly InvoicePaymentMethodType Check = new("check");

    public static readonly InvoicePaymentMethodType Cash = new("cash");

    public static readonly InvoicePaymentMethodType MoneyOrder = new("money_order");

    public static readonly InvoicePaymentMethodType Ach = new("ach");

    public static readonly InvoicePaymentMethodType Other = new("other");

    public static InvoicePaymentMethodType FromValue(string value) => FromValueCore(value);
}
