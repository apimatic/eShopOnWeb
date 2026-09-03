using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The channel type. One of <c>web</c>, <c>facebook</c>, <c>sms</c>, <c>whatsapp</c>, <c>line</c> or <c>custom</c>. By default, Studio’s Send to Flex widget passes it on to the Task attributes for Tasks created based on this Flex Flow. The Task attributes will be used by the Flex UI to render the respective Task as appropriate (applying channel-specific design and length limits). If <c>channelType</c> is <c>facebook</c>, <c>whatsapp</c> or <c>line</c>, the Send to Flex widget should set the Task Channel to Programmable Chat.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<FlexFlowEnumChannelType>))]
public sealed record FlexFlowEnumChannelType : StringEnum<FlexFlowEnumChannelType>
{
    private FlexFlowEnumChannelType(string value) : base(value)
    {
    }

    public static readonly FlexFlowEnumChannelType Web = new("web");

    public static readonly FlexFlowEnumChannelType Sms = new("sms");

    public static readonly FlexFlowEnumChannelType Facebook = new("facebook");

    public static readonly FlexFlowEnumChannelType Whatsapp = new("whatsapp");

    public static readonly FlexFlowEnumChannelType Line = new("line");

    public static readonly FlexFlowEnumChannelType Custom = new("custom");

    public static FlexFlowEnumChannelType FromValue(string value) => FromValueCore(value);
}
