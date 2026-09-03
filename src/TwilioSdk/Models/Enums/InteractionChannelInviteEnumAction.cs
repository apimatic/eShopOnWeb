using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<InteractionChannelInviteEnumAction>))]
public sealed record InteractionChannelInviteEnumAction : StringEnum<InteractionChannelInviteEnumAction>
{
    private InteractionChannelInviteEnumAction(string value) : base(value)
    {
    }

    public static readonly InteractionChannelInviteEnumAction Accept = new("accept");

    public static readonly InteractionChannelInviteEnumAction Decline = new("decline");

    public static InteractionChannelInviteEnumAction FromValue(string value) => FromValueCore(value);
}
