using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Type of bank account if payment source is ACH. One of <c>consumer-checking</c>, <c>consumer-savings</c>, or <c>commercial-checking</c>. The default value is <c>consumer-checking</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PaymentsEnumBankAccountType>))]
public sealed record PaymentsEnumBankAccountType : StringEnum<PaymentsEnumBankAccountType>
{
    private PaymentsEnumBankAccountType(string value) : base(value)
    {
    }

    public static readonly PaymentsEnumBankAccountType ConsumerChecking = new("consumer-checking");

    public static readonly PaymentsEnumBankAccountType ConsumerSavings = new("consumer-savings");

    public static readonly PaymentsEnumBankAccountType CommercialChecking = new("commercial-checking");

    public static PaymentsEnumBankAccountType FromValue(string value) => FromValueCore(value);
}
