using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<InsightsConversationalAiEnumScoreId>))]
public sealed record InsightsConversationalAiEnumScoreId : StringEnum<InsightsConversationalAiEnumScoreId>
{
    private InsightsConversationalAiEnumScoreId(string value) : base(value)
    {
    }

    public static readonly InsightsConversationalAiEnumScoreId PredictedCsat = new("~predicted-csat");

    public static readonly InsightsConversationalAiEnumScoreId AgentExperience = new("~agent-experience");

    public static readonly InsightsConversationalAiEnumScoreId CustomerEffort = new("~customer-effort");

    public static readonly InsightsConversationalAiEnumScoreId MultitouchRisk = new("~multitouch-risk");

    public static InsightsConversationalAiEnumScoreId FromValue(string value) => FromValueCore(value);
}
