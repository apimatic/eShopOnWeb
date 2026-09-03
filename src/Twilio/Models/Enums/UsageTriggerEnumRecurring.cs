using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The frequency of a recurring UsageTrigger.  Can be: <c>daily</c>, <c>monthly</c>, or <c>yearly</c> for recurring triggers or empty for non-recurring triggers. A trigger will only fire once during each period. Recurring times are in GMT.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<UsageTriggerEnumRecurring>))]
public sealed record UsageTriggerEnumRecurring : StringEnum<UsageTriggerEnumRecurring>
{
    private UsageTriggerEnumRecurring(string value) : base(value)
    {
    }

    public static readonly UsageTriggerEnumRecurring Daily = new("daily");

    public static readonly UsageTriggerEnumRecurring Monthly = new("monthly");

    public static readonly UsageTriggerEnumRecurring Yearly = new("yearly");

    public static readonly UsageTriggerEnumRecurring Alltime = new("alltime");

    public static UsageTriggerEnumRecurring FromValue(string value) => FromValueCore(value);
}
