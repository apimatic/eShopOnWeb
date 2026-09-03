using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Type of Participant in the Conversation.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Type11>))]
public sealed record Type11 : StringEnum<Type11>
{
    private Type11(string value) : base(value)
    {
    }

    public static readonly Type11 HumanAgent = new("HUMAN_AGENT");

    public static readonly Type11 Customer = new("CUSTOMER");

    public static readonly Type11 AiAgent = new("AI_AGENT");

    public static Type11 FromValue(string value) => FromValueCore(value);
}
