using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The metric this rule is associated with.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<FlexInsightsRulesEnumMetricId>))]
public sealed record FlexInsightsRulesEnumMetricId : StringEnum<FlexInsightsRulesEnumMetricId>
{
    private FlexInsightsRulesEnumMetricId(string value) : base(value)
    {
    }

    public static readonly FlexInsightsRulesEnumMetricId ActiveNow = new("Active (Now)");

    public static readonly FlexInsightsRulesEnumMetricId WaitingNow = new("Waiting (Now)");

    public static readonly FlexInsightsRulesEnumMetricId AvailableAgentsNow = new("Available Agents (Now)");

    public static readonly FlexInsightsRulesEnumMetricId OfflineAgentsNow = new("Offline Agents (Now)");

    public static readonly FlexInsightsRulesEnumMetricId UnavailableAgentsNow = new("Unavailable Agents (Now)");

    public static readonly FlexInsightsRulesEnumMetricId Abandoned30Min = new("Abandoned (30 min)");

    public static readonly FlexInsightsRulesEnumMetricId AbandonedToday = new("Abandoned (Today)");

    public static readonly FlexInsightsRulesEnumMetricId Accepted30Min = new("Accepted (30 min)");

    public static readonly FlexInsightsRulesEnumMetricId AcceptedToday = new("Accepted (Today)");

    public static readonly FlexInsightsRulesEnumMetricId AvgSpeedOfAnswerToday = new("Avg. Speed of Answer (Today)");

    public static readonly FlexInsightsRulesEnumMetricId AvgHandleTimeToday = new("Avg. Handle Time (Today)");

    public static readonly FlexInsightsRulesEnumMetricId MissedInvitations30Min = new("Missed Invitations (30 min)");

    public static readonly FlexInsightsRulesEnumMetricId MissedInvitationsToday = new("Missed Invitations (Today)");

    public static readonly FlexInsightsRulesEnumMetricId Sla30Min = new("SLA (30 min)");

    public static readonly FlexInsightsRulesEnumMetricId SlaToday = new("SLA (Today)");

    public static readonly FlexInsightsRulesEnumMetricId LongestAvailableAgentNow = new("Longest Available Agent (Now)");

    public static readonly FlexInsightsRulesEnumMetricId LongestNow = new("Longest (Now)");

    public static FlexInsightsRulesEnumMetricId FromValue(string value) => FromValueCore(value);
}
