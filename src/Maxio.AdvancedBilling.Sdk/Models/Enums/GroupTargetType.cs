using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// The type of object indicated by the id attribute.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<GroupTargetType>))]
public sealed record GroupTargetType : StringEnum<GroupTargetType>
{
    private GroupTargetType(string value) : base(value)
    {
    }

    public static readonly GroupTargetType Customer = new("customer");

    public static readonly GroupTargetType Subscription = new("subscription");

    public static readonly GroupTargetType Self = new("self");

    public static readonly GroupTargetType Parent = new("parent");

    public static readonly GroupTargetType Eldest = new("eldest");

    public static GroupTargetType FromValue(string value) => FromValueCore(value);
}
