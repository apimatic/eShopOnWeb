using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The pricing model for tiered plan. The <c>tiers</c> parameter is required.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SubscriptionPricingModel>))]
public sealed record SubscriptionPricingModel : OpenStringEnum<SubscriptionPricingModel>
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

    public TResult Match<TResult>(Func<TResult> onVolume, Func<TResult> onTiered, Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Volume => onVolume(),
            _ when this == Tiered => onTiered(),
            _ => otherwise(Value)
        };

    public void Match(Action onVolume, Action onTiered, Action<string> otherwise)
    {
        if (this == Volume) onVolume();
        else if (this == Tiered) onTiered();
        else otherwise(Value);
    }
}
