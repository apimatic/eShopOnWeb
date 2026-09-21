using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<GroupType>))]
public sealed record GroupType : StringEnum<GroupType>
{
    private GroupType(string value) : base(value)
    {
    }

    public static readonly GroupType SingleCustomer = new("single_customer");

    public static readonly GroupType MultipleCustomers = new("multiple_customers");

    public static GroupType FromValue(string value) => FromValueCore(value);
}
