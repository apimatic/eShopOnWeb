using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Sort order direction, ascending or descending
/// </summary>
[JsonConverter(typeof(StringEnumConverter<BundleEnumSortDirection>))]
public sealed record BundleEnumSortDirection : StringEnum<BundleEnumSortDirection>
{
    private BundleEnumSortDirection(string value) : base(value)
    {
    }

    public static readonly BundleEnumSortDirection Asc = new("ASC");

    public static readonly BundleEnumSortDirection Desc = new("DESC");

    public static BundleEnumSortDirection FromValue(string value) => FromValueCore(value);
}
