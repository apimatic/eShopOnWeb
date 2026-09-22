using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

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
