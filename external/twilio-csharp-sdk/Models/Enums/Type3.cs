using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Type of Participant in the Conversation.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Type3>))]
public sealed record Type3 : StringEnum<Type3>
{
    private Type3(string value) : base(value)
    {
    }

    public static readonly Type3 HumanAgent = new("HUMAN_AGENT");

    public static readonly Type3 Customer = new("CUSTOMER");

    public static readonly Type3 AiAgent = new("AI_AGENT");

    public static Type3 FromValue(string value) => FromValueCore(value);
}
