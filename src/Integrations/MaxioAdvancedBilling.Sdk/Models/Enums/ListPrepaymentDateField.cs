using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ListPrepaymentDateField>))]
public sealed record ListPrepaymentDateField : StringEnum<ListPrepaymentDateField>
{
    private ListPrepaymentDateField(string value) : base(value)
    {
    }

    public static readonly ListPrepaymentDateField CreatedAt = new("created_at");

    public static readonly ListPrepaymentDateField ApplicationAt = new("application_at");

    public static ListPrepaymentDateField FromValue(string value) => FromValueCore(value);
}
