using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<InsightsConversationalAiEnumName>))]
public sealed record InsightsConversationalAiEnumName : StringEnum<InsightsConversationalAiEnumName>
{
    private InsightsConversationalAiEnumName(string value) : base(value)
    {
    }

    public static readonly InsightsConversationalAiEnumName PredictiveScores = new("predictive_scores");

    public static readonly InsightsConversationalAiEnumName ChannelMetrics = new("channel_metrics");

    public static readonly InsightsConversationalAiEnumName AgentMetrics = new("agent_metrics");

    public static readonly InsightsConversationalAiEnumName QueueMetrics = new("queue_metrics");

    public static readonly InsightsConversationalAiEnumName AgentsCsatSummary = new("agents_csat_summary");

    public static readonly InsightsConversationalAiEnumName TopicMetrics = new("topic_metrics");

    public static readonly InsightsConversationalAiEnumName ConversationMetrics = new("conversation_metrics");

    public static readonly InsightsConversationalAiEnumName TrendMetrics = new("trend_metrics");

    public static InsightsConversationalAiEnumName FromValue(string value) => FromValueCore(value);
}
