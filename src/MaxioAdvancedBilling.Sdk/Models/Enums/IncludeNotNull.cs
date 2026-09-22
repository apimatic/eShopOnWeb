using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// Passed as a parameter to list methods to return only non null values.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<IncludeNotNull>))]
public sealed record IncludeNotNull : StringEnum<IncludeNotNull>
{
    private IncludeNotNull(string value) : base(value)
    {
    }

    public static readonly IncludeNotNull NotNull = new("not_null");

    public static IncludeNotNull FromValue(string value) => FromValueCore(value);
}
