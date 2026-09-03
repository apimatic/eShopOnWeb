using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<InsightsConversationalAiEnumType>))]
public sealed record InsightsConversationalAiEnumType : StringEnum<InsightsConversationalAiEnumType>
{
    private InsightsConversationalAiEnumType(string value) : base(value)
    {
    }

    public static readonly InsightsConversationalAiEnumType Metrics = new("metrics");

    public static readonly InsightsConversationalAiEnumType Summary = new("summary");

    public static readonly InsightsConversationalAiEnumType Trend = new("trend");

    public static InsightsConversationalAiEnumType FromValue(string value) => FromValueCore(value);
}
