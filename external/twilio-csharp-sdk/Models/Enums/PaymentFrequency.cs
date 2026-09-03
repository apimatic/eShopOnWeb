using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<PaymentFrequency>))]
public sealed record PaymentFrequency : StringEnum<PaymentFrequency>
{
    private PaymentFrequency(string value) : base(value)
    {
    }

    public static readonly PaymentFrequency Quarterly = new("QUARTERLY");

    public static readonly PaymentFrequency Yearly = new("YEARLY");

    public static PaymentFrequency FromValue(string value) => FromValueCore(value);
}
