using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ListEventsDateField>))]
public sealed record ListEventsDateField : StringEnum<ListEventsDateField>
{
    private ListEventsDateField(string value) : base(value)
    {
    }

    public static readonly ListEventsDateField CreatedAt = new("created_at");

    public static ListEventsDateField FromValue(string value) => FromValueCore(value);
}
