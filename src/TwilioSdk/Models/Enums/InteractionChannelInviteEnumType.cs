using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<InteractionChannelInviteEnumType>))]
public sealed record InteractionChannelInviteEnumType : StringEnum<InteractionChannelInviteEnumType>
{
    private InteractionChannelInviteEnumType(string value) : base(value)
    {
    }

    public static readonly InteractionChannelInviteEnumType Taskrouter = new("taskrouter");

    public static InteractionChannelInviteEnumType FromValue(string value) => FromValueCore(value);
}
