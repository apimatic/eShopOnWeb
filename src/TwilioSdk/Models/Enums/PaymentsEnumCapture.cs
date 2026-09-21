using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The piece of payment information that you wish the caller to enter. Must be one of <c>payment-card-number</c>, <c>expiration-date</c>, <c>security-code</c>, <c>postal-code</c>, <c>bank-routing-number</c>, <c>bank-account-number</c>, or their <c>-matcher</c> variants for input confirmation when <c>RequireMatchingInputs</c> is enabled.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PaymentsEnumCapture>))]
public sealed record PaymentsEnumCapture : StringEnum<PaymentsEnumCapture>
{
    private PaymentsEnumCapture(string value) : base(value)
    {
    }

    public static readonly PaymentsEnumCapture PaymentCardNumber = new("payment-card-number");

    public static readonly PaymentsEnumCapture ExpirationDate = new("expiration-date");

    public static readonly PaymentsEnumCapture SecurityCode = new("security-code");

    public static readonly PaymentsEnumCapture PostalCode = new("postal-code");

    public static readonly PaymentsEnumCapture BankRoutingNumber = new("bank-routing-number");

    public static readonly PaymentsEnumCapture BankAccountNumber = new("bank-account-number");

    public static readonly PaymentsEnumCapture PaymentCardNumberMatcher = new("payment-card-number-matcher");

    public static readonly PaymentsEnumCapture ExpirationDateMatcher = new("expiration-date-matcher");

    public static readonly PaymentsEnumCapture SecurityCodeMatcher = new("security-code-matcher");

    public static readonly PaymentsEnumCapture PostalCodeMatcher = new("postal-code-matcher");

    public static PaymentsEnumCapture FromValue(string value) => FromValueCore(value);
}
