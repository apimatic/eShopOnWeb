using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<CampaignFrequency>))]
public sealed record CampaignFrequency : StringEnum<CampaignFrequency>
{
    private CampaignFrequency(string value) : base(value)
    {
    }

    public static readonly CampaignFrequency OneMessagePerSignup = new("ONE_MESSAGE_PER_SIGNUP");

    public static readonly CampaignFrequency LimitedNumberOfMessagesPerSignup = new("LIMITED_NUMBER_OF_MESSAGES_PER_SIGNUP");

    public static readonly CampaignFrequency LimitedNumberOfMessagesAtRecurringIntervals = new("LIMITED_NUMBER_OF_MESSAGES_AT_RECURRING_INTERVALS");

    public static readonly CampaignFrequency VariableNumberOfMessagesAtUnpredictableIntervals = new("VARIABLE_NUMBER_OF_MESSAGES_AT_UNPREDICTABLE_INTERVALS");

    public static readonly CampaignFrequency LimitedNumberOfMessagesInResponseToKeywords = new("LIMITED_NUMBER_OF_MESSAGES_IN_RESPONSE_TO_KEYWORDS");

    public static CampaignFrequency FromValue(string value) => FromValueCore(value);
}
