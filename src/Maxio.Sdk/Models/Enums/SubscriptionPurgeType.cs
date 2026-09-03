using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SubscriptionPurgeType>))]
public sealed record SubscriptionPurgeType : StringEnum<SubscriptionPurgeType>
{
    private SubscriptionPurgeType(string value) : base(value)
    {
    }

    public static readonly SubscriptionPurgeType Customer = new("customer");

    public static readonly SubscriptionPurgeType PaymentProfile = new("payment_profile");

    public static SubscriptionPurgeType FromValue(string value) => FromValueCore(value);
}
