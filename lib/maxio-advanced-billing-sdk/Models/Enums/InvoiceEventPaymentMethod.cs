using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<InvoiceEventPaymentMethod>))]
public sealed record InvoiceEventPaymentMethod : StringEnum<InvoiceEventPaymentMethod>
{
    private InvoiceEventPaymentMethod(string value) : base(value)
    {
    }

    public static readonly InvoiceEventPaymentMethod ApplePay = new("apple_pay");

    public static readonly InvoiceEventPaymentMethod BankAccount = new("bank_account");

    public static readonly InvoiceEventPaymentMethod CreditCard = new("credit_card");

    public static readonly InvoiceEventPaymentMethod External = new("external");

    public static readonly InvoiceEventPaymentMethod PaypalAccount = new("paypal_account");

    public static InvoiceEventPaymentMethod FromValue(string value) => FromValueCore(value);
}
