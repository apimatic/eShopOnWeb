using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The strategy Conversation Orchestrator uses to assign communications to conversations.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ConversationGroupingType3>))]
public sealed record ConversationGroupingType3 : StringEnum<ConversationGroupingType3>
{
    private ConversationGroupingType3(string value) : base(value)
    {
    }

    public static readonly ConversationGroupingType3 GroupByProfile = new("GROUP_BY_PROFILE");

    public static readonly ConversationGroupingType3 GroupByParticipantAddresses = new("GROUP_BY_PARTICIPANT_ADDRESSES");

    public static readonly ConversationGroupingType3 GroupByParticipantAddressesAndChannelType = new("GROUP_BY_PARTICIPANT_ADDRESSES_AND_CHANNEL_TYPE");

    public static ConversationGroupingType3 FromValue(string value) => FromValueCore(value);
}
