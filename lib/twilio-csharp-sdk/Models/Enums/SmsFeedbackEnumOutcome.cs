using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SmsFeedbackEnumOutcome>))]
public sealed record SmsFeedbackEnumOutcome : StringEnum<SmsFeedbackEnumOutcome>
{
    private SmsFeedbackEnumOutcome(string value) : base(value)
    {
    }

    public static readonly SmsFeedbackEnumOutcome Confirmed = new("confirmed");

    public static readonly SmsFeedbackEnumOutcome Unconfirmed = new("unconfirmed");

    public static readonly SmsFeedbackEnumOutcome Received = new("received");

    public static readonly SmsFeedbackEnumOutcome NotReceived = new("not-received");

    public static readonly SmsFeedbackEnumOutcome Delayed = new("delayed");

    public static SmsFeedbackEnumOutcome FromValue(string value) => FromValueCore(value);
}
