using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ResourceType>))]
public sealed record ResourceType : StringEnum<ResourceType>
{
    private ResourceType(string value) : base(value)
    {
    }

    public static readonly ResourceType Subscriptions = new("subscriptions");

    public static readonly ResourceType Customers = new("customers");

    public static ResourceType FromValue(string value) => FromValueCore(value);
}
