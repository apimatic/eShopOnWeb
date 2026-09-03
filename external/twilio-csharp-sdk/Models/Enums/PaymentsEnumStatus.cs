using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Indicates whether the current payment session should be cancelled or completed. When <c>cancel</c> the payment session is cancelled. When <c>complete</c>, Twilio sends the payment information to the selected Pay Connector for processing.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PaymentsEnumStatus>))]
public sealed record PaymentsEnumStatus : StringEnum<PaymentsEnumStatus>
{
    private PaymentsEnumStatus(string value) : base(value)
    {
    }

    public static readonly PaymentsEnumStatus Complete = new("complete");

    public static readonly PaymentsEnumStatus Cancel = new("cancel");

    public static PaymentsEnumStatus FromValue(string value) => FromValueCore(value);
}
