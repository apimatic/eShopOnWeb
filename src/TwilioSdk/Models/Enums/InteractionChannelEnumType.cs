using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The Interaction Channel's type. Can be: <c>sms</c>, <c>email</c>, <c>chat</c>, <c>whatsapp</c>, <c>web</c>, <c>messenger</c>, or <c>gbm</c>.
///  <b>Note:</b> These can be different from the task channel type specified in the Routing attributes. Task channel type corresponds to channel capacity while this channel type is the actual media type
/// </summary>
[JsonConverter(typeof(StringEnumConverter<InteractionChannelEnumType>))]
public sealed record InteractionChannelEnumType : StringEnum<InteractionChannelEnumType>
{
    private InteractionChannelEnumType(string value) : base(value)
    {
    }

    public static readonly InteractionChannelEnumType Voice = new("voice");

    public static readonly InteractionChannelEnumType Sms = new("sms");

    public static readonly InteractionChannelEnumType Email = new("email");

    public static readonly InteractionChannelEnumType Web = new("web");

    public static readonly InteractionChannelEnumType Whatsapp = new("whatsapp");

    public static readonly InteractionChannelEnumType Chat = new("chat");

    public static readonly InteractionChannelEnumType Messenger = new("messenger");

    public static readonly InteractionChannelEnumType Gbm = new("gbm");

    public static InteractionChannelEnumType FromValue(string value) => FromValueCore(value);
}
