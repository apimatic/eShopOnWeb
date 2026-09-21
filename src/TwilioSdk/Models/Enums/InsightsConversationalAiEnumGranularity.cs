using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<InsightsConversationalAiEnumGranularity>))]
public sealed record InsightsConversationalAiEnumGranularity : StringEnum<InsightsConversationalAiEnumGranularity>
{
    private InsightsConversationalAiEnumGranularity(string value) : base(value)
    {
    }

    public static readonly InsightsConversationalAiEnumGranularity Days = new("days");

    public static readonly InsightsConversationalAiEnumGranularity Weeks = new("weeks");

    public static readonly InsightsConversationalAiEnumGranularity Months = new("months");

    public static readonly InsightsConversationalAiEnumGranularity Quarters = new("quarters");

    public static readonly InsightsConversationalAiEnumGranularity Years = new("years");

    public static InsightsConversationalAiEnumGranularity FromValue(string value) => FromValueCore(value);
}
