using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Indicates whether the payment method should be tokenized as a <c>one-time</c>, <c>reusable</c>, or <c>payment-method</c> token. The default value is <c>reusable</c>. Do not enter a charge amount when tokenizing. If a charge amount is entered, the payment method will be charged and not tokenized.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PaymentsEnumTokenType>))]
public sealed record PaymentsEnumTokenType : StringEnum<PaymentsEnumTokenType>
{
    private PaymentsEnumTokenType(string value) : base(value)
    {
    }

    public static readonly PaymentsEnumTokenType OneTime = new("one-time");

    public static readonly PaymentsEnumTokenType Reusable = new("reusable");

    public static readonly PaymentsEnumTokenType PaymentMethod = new("payment-method");

    public static PaymentsEnumTokenType FromValue(string value) => FromValueCore(value);
}
