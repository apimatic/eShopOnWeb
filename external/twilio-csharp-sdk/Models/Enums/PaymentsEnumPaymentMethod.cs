using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Type of payment being captured. One of <c>credit-card</c> or <c>ach-debit</c>. The default value is <c>credit-card</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PaymentsEnumPaymentMethod>))]
public sealed record PaymentsEnumPaymentMethod : StringEnum<PaymentsEnumPaymentMethod>
{
    private PaymentsEnumPaymentMethod(string value) : base(value)
    {
    }

    public static readonly PaymentsEnumPaymentMethod CreditCard = new("credit-card");

    public static readonly PaymentsEnumPaymentMethod AchDebit = new("ach-debit");

    public static PaymentsEnumPaymentMethod FromValue(string value) => FromValueCore(value);
}
