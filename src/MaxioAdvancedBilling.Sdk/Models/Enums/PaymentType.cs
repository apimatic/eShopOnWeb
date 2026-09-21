using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<PaymentType>))]
public sealed record PaymentType : StringEnum<PaymentType>
{
    private PaymentType(string value) : base(value)
    {
    }

    public static readonly PaymentType CreditCard = new("credit_card");

    public static readonly PaymentType BankAccount = new("bank_account");

    public static readonly PaymentType PaypalAccount = new("paypal_account");

    public static readonly PaymentType ApplePay = new("apple_pay");

    public static PaymentType FromValue(string value) => FromValueCore(value);
}
