using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<InteractionChannelAppEnumStatus>))]
public sealed record InteractionChannelAppEnumStatus : StringEnum<InteractionChannelAppEnumStatus>
{
    private InteractionChannelAppEnumStatus(string value) : base(value)
    {
    }

    public static readonly InteractionChannelAppEnumStatus Adding = new("adding");

    public static readonly InteractionChannelAppEnumStatus Active = new("active");

    public static readonly InteractionChannelAppEnumStatus Pausing = new("pausing");

    public static readonly InteractionChannelAppEnumStatus Paused = new("paused");

    public static readonly InteractionChannelAppEnumStatus Resuming = new("resuming");

    public static readonly InteractionChannelAppEnumStatus Removing = new("removing");

    public static readonly InteractionChannelAppEnumStatus Removed = new("removed");

    public static readonly InteractionChannelAppEnumStatus Errored = new("errored");

    public static InteractionChannelAppEnumStatus FromValue(string value) => FromValueCore(value);
}
