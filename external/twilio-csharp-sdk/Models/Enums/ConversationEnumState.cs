using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Current state of this conversation. Can be either <c>initializing</c>, <c>active</c>, <c>inactive</c> or <c>closed</c> and defaults to <c>active</c>
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ConversationEnumState>))]
public sealed record ConversationEnumState : StringEnum<ConversationEnumState>
{
    private ConversationEnumState(string value) : base(value)
    {
    }

    public static readonly ConversationEnumState Initializing = new("initializing");

    public static readonly ConversationEnumState Inactive = new("inactive");

    public static readonly ConversationEnumState Active = new("active");

    public static readonly ConversationEnumState Closed = new("closed");

    public static ConversationEnumState FromValue(string value) => FromValueCore(value);
}
