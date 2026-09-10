using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<CustomFieldOwner>))]
public sealed record CustomFieldOwner : StringEnum<CustomFieldOwner>
{
    private CustomFieldOwner(string value) : base(value)
    {
    }

    public static readonly CustomFieldOwner Customer = new("Customer");

    public static readonly CustomFieldOwner Subscription = new("Subscription");

    public static CustomFieldOwner FromValue(string value) => FromValueCore(value);
}
