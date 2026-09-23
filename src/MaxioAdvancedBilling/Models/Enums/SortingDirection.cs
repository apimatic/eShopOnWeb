using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// Used for sorting results.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SortingDirection>))]
public sealed record SortingDirection : StringEnum<SortingDirection>
{
    private SortingDirection(string value) : base(value)
    {
    }

    public static readonly SortingDirection Asc = new("asc");

    public static readonly SortingDirection Desc = new("desc");

    public static SortingDirection FromValue(string value) => FromValueCore(value);
}
