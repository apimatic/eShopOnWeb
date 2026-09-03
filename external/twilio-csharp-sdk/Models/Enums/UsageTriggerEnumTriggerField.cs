using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The field in the <see href="https://www.twilio.com/docs/usage/api/usage-record">UsageRecord</see> resource that fires the trigger.  Can be: <c>count</c>, <c>usage</c>, or <c>price</c>, as described in the <see href="https://www.twilio.com/docs/usage/api/usage-record#usage-count-price">UsageRecords documentation</see>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<UsageTriggerEnumTriggerField>))]
public sealed record UsageTriggerEnumTriggerField : StringEnum<UsageTriggerEnumTriggerField>
{
    private UsageTriggerEnumTriggerField(string value) : base(value)
    {
    }

    public static readonly UsageTriggerEnumTriggerField Count = new("count");

    public static readonly UsageTriggerEnumTriggerField Usage = new("usage");

    public static readonly UsageTriggerEnumTriggerField Price = new("price");

    public static UsageTriggerEnumTriggerField FromValue(string value) => FromValueCore(value);
}
