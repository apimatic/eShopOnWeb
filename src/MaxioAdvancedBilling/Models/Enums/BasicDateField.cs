using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// Allows to filter by <c>created_at</c> or <c>updated_at</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<BasicDateField>))]
public sealed record BasicDateField : StringEnum<BasicDateField>
{
    private BasicDateField(string value) : base(value)
    {
    }

    public static readonly BasicDateField UpdatedAt = new("updated_at");

    public static readonly BasicDateField CreatedAt = new("created_at");

    public static BasicDateField FromValue(string value) => FromValueCore(value);
}
