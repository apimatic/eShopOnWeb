using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The minimum severity level that will trigger a notification.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<FlexInsightsRulesEnumNotifySeverity>))]
public sealed record FlexInsightsRulesEnumNotifySeverity : StringEnum<FlexInsightsRulesEnumNotifySeverity>
{
    private FlexInsightsRulesEnumNotifySeverity(string value) : base(value)
    {
    }

    public static readonly FlexInsightsRulesEnumNotifySeverity Critical = new("Critical");

    public static readonly FlexInsightsRulesEnumNotifySeverity Warning = new("Warning");

    public static FlexInsightsRulesEnumNotifySeverity FromValue(string value) => FromValueCore(value);
}
