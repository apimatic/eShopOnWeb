using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type5>))]
public sealed record Type5 : StringEnum<Type5>
{
    private Type5(string value) : base(value)
    {
    }

    public static readonly Type5 HumanAgent = new("HUMAN_AGENT");

    public static readonly Type5 Customer = new("CUSTOMER");

    public static readonly Type5 AiAgent = new("AI_AGENT");

    public static readonly Type5 Agent = new("AGENT");

    public static readonly Type5 Unknown = new("UNKNOWN");

    public static Type5 FromValue(string value) => FromValueCore(value);
}
