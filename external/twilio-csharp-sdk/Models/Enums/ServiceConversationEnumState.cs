using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Current state of this conversation. Can be either <c>initializing</c>, <c>active</c>, <c>inactive</c> or <c>closed</c> and defaults to <c>active</c>
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ServiceConversationEnumState>))]
public sealed record ServiceConversationEnumState : StringEnum<ServiceConversationEnumState>
{
    private ServiceConversationEnumState(string value) : base(value)
    {
    }

    public static readonly ServiceConversationEnumState Inactive = new("inactive");

    public static readonly ServiceConversationEnumState Active = new("active");

    public static readonly ServiceConversationEnumState Closed = new("closed");

    public static readonly ServiceConversationEnumState Initializing = new("initializing");

    public static ServiceConversationEnumState FromValue(string value) => FromValueCore(value);
}
