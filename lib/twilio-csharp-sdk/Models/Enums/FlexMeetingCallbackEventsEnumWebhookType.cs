using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<FlexMeetingCallbackEventsEnumWebhookType>))]
public sealed record FlexMeetingCallbackEventsEnumWebhookType : StringEnum<FlexMeetingCallbackEventsEnumWebhookType>
{
    private FlexMeetingCallbackEventsEnumWebhookType(string value) : base(value)
    {
    }

    public static readonly FlexMeetingCallbackEventsEnumWebhookType Global = new("global");

    public static readonly FlexMeetingCallbackEventsEnumWebhookType Interaction = new("interaction");

    public static FlexMeetingCallbackEventsEnumWebhookType FromValue(string value) => FromValueCore(value);
}
