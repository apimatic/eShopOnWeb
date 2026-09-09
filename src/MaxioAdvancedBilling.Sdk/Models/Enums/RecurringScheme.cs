using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<RecurringScheme>))]
public sealed record RecurringScheme : StringEnum<RecurringScheme>
{
    private RecurringScheme(string value) : base(value)
    {
    }

    public static readonly RecurringScheme DoNotRecur = new("do_not_recur");

    public static readonly RecurringScheme RecurIndefinitely = new("recur_indefinitely");

    public static readonly RecurringScheme RecurWithDuration = new("recur_with_duration");

    public static RecurringScheme FromValue(string value) => FromValueCore(value);
}
