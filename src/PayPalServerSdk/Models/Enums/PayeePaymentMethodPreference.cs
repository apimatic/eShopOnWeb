using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The merchant-preferred payment methods.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PayeePaymentMethodPreference>))]
public sealed record PayeePaymentMethodPreference : StringEnum<PayeePaymentMethodPreference>
{
    private PayeePaymentMethodPreference(string value) : base(value)
    {
    }

    /// <summary>
    /// Accepts any type of payment from the customer.
    /// </summary>
    public static readonly PayeePaymentMethodPreference Unrestricted = new("UNRESTRICTED");

    /// <summary>
    /// Accepts only immediate payment from the customer. For example, credit card, PayPal balance, or instant ACH. Ensures that at the time of capture, the payment does not have the <c>pending</c> status.
    /// </summary>
    public static readonly PayeePaymentMethodPreference ImmediatePaymentRequired = new("IMMEDIATE_PAYMENT_REQUIRED");

    public static PayeePaymentMethodPreference FromValue(string value) => FromValueCore(value);
}
