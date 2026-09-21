using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// Allows to filter by <c>not_null</c> or <c>null</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<IncludeNullOrNotNull>))]
public sealed record IncludeNullOrNotNull : StringEnum<IncludeNullOrNotNull>
{
    private IncludeNullOrNotNull(string value) : base(value)
    {
    }

    public static readonly IncludeNullOrNotNull NotNull = new("not_null");

    public static readonly IncludeNullOrNotNull Null = new("null");

    public static IncludeNullOrNotNull FromValue(string value) => FromValueCore(value);
}
