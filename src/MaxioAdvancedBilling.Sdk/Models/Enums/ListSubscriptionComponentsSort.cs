using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ListSubscriptionComponentsSort>))]
public sealed record ListSubscriptionComponentsSort : StringEnum<ListSubscriptionComponentsSort>
{
    private ListSubscriptionComponentsSort(string value) : base(value)
    {
    }

    public static readonly ListSubscriptionComponentsSort Id = new("id");

    public static readonly ListSubscriptionComponentsSort UpdatedAt = new("updated_at");

    public static ListSubscriptionComponentsSort FromValue(string value) => FromValueCore(value);
}
