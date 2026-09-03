using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Type of Conversation grouping strategy:
/// - <c>GROUP_BY_PROFILE</c>: Groups Communications by resolved Profile from the Memory Store.
///   A Profile is looked up or created for <c>CUSTOMER</c> Participant types. All Communications from the same Profile are in the same Conversation, regardless of address or channel.
/// - <c>GROUP_BY_PARTICIPANT_ADDRESSES</c>: Groups Communications by Participant addresses across all channels.
///   A customer using +18005550100 will be in the same Conversation whether they contact by SMS, WhatsApp, or RCS.
/// - <c>GROUP_BY_PARTICIPANT_ADDRESSES_AND_CHANNEL_TYPE</c>: Groups Communications by both Participant addresses AND channel.
///   A customer using +18005550100 by SMS will be in a different Conversation than the same customer by Voice.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ConversationGroupingType>))]
public sealed record ConversationGroupingType : StringEnum<ConversationGroupingType>
{
    private ConversationGroupingType(string value) : base(value)
    {
    }

    public static readonly ConversationGroupingType GroupByProfile = new("GROUP_BY_PROFILE");

    public static readonly ConversationGroupingType GroupByParticipantAddresses = new("GROUP_BY_PARTICIPANT_ADDRESSES");

    public static readonly ConversationGroupingType GroupByParticipantAddressesAndChannelType = new("GROUP_BY_PARTICIPANT_ADDRESSES_AND_CHANNEL_TYPE");

    public static ConversationGroupingType FromValue(string value) => FromValueCore(value);
}
