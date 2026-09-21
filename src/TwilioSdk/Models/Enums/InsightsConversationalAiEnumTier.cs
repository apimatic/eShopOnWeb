using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<InsightsConversationalAiEnumTier>))]
public sealed record InsightsConversationalAiEnumTier : StringEnum<InsightsConversationalAiEnumTier>
{
    private InsightsConversationalAiEnumTier(string value) : base(value)
    {
    }

    public static readonly InsightsConversationalAiEnumTier Low = new("Low");

    public static readonly InsightsConversationalAiEnumTier High = new("High");

    public static readonly InsightsConversationalAiEnumTier Neutral = new("Neutral");

    public static InsightsConversationalAiEnumTier FromValue(string value) => FromValueCore(value);
}
