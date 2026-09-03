using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SubscriptionListDateField>))]
public sealed record SubscriptionListDateField : StringEnum<SubscriptionListDateField>
{
    private SubscriptionListDateField(string value) : base(value)
    {
    }

    public static readonly SubscriptionListDateField UpdatedAt = new("updated_at");

    public static SubscriptionListDateField FromValue(string value) => FromValueCore(value);
}
