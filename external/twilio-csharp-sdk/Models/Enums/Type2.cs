using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Type of Participant in the Conversation.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Type2>))]
public sealed record Type2 : StringEnum<Type2>
{
    private Type2(string value) : base(value)
    {
    }

    public static readonly Type2 HumanAgent = new("HUMAN_AGENT");

    public static readonly Type2 Customer = new("CUSTOMER");

    public static readonly Type2 AiAgent = new("AI_AGENT");

    public static readonly Type2 Agent = new("AGENT");

    public static readonly Type2 Unknown = new("UNKNOWN");

    public static Type2 FromValue(string value) => FromValueCore(value);
}
