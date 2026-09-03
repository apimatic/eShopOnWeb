using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Reported outcome indicating whether there is confirmation that the Message recipient performed a tracked user action. Can be: <c>unconfirmed</c> or <c>confirmed</c>. For more details see <see href="https://www.twilio.com/docs/messaging/guides/send-message-feedback-to-twilio">How to Optimize Message Deliverability with Message Feedback</see>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<MessageFeedbackEnumOutcome>))]
public sealed record MessageFeedbackEnumOutcome : StringEnum<MessageFeedbackEnumOutcome>
{
    private MessageFeedbackEnumOutcome(string value) : base(value)
    {
    }

    public static readonly MessageFeedbackEnumOutcome Confirmed = new("confirmed");

    public static readonly MessageFeedbackEnumOutcome Unconfirmed = new("unconfirmed");

    public static MessageFeedbackEnumOutcome FromValue(string value) => FromValueCore(value);
}
