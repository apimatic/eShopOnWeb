using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<InsightsConversationalAiEnumSortBy>))]
public sealed record InsightsConversationalAiEnumSortBy : StringEnum<InsightsConversationalAiEnumSortBy>
{
    private InsightsConversationalAiEnumSortBy(string value) : base(value)
    {
    }

    public static readonly InsightsConversationalAiEnumSortBy RecordCount = new("record_count");

    public static readonly InsightsConversationalAiEnumSortBy ScoredCount = new("scored_count");

    public static readonly InsightsConversationalAiEnumSortBy Total = new("total");

    public static readonly InsightsConversationalAiEnumSortBy Mean = new("mean");

    public static readonly InsightsConversationalAiEnumSortBy ScoredMean = new("scored_mean");

    public static readonly InsightsConversationalAiEnumSortBy Score = new("score");

    public static InsightsConversationalAiEnumSortBy FromValue(string value) => FromValueCore(value);
}
