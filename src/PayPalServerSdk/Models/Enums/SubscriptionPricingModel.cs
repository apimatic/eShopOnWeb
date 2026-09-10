using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The pricing model for tiered plan. The <c>tiers</c> parameter is required.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SubscriptionPricingModel>))]
public sealed record SubscriptionPricingModel : StringEnum<SubscriptionPricingModel>
{
    private SubscriptionPricingModel(string value) : base(value)
    {
    }

    /// <summary>
    /// A volume pricing model.
    /// </summary>
    public static readonly SubscriptionPricingModel Volume = new("VOLUME");

    /// <summary>
    /// A tiered pricing model.
    /// </summary>
    public static readonly SubscriptionPricingModel Tiered = new("TIERED");

    public static SubscriptionPricingModel FromValue(string value) => FromValueCore(value);
}
