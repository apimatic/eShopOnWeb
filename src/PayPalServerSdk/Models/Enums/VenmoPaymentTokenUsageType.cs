using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The usage type associated with the Venmo payment token.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VenmoPaymentTokenUsageType>))]
public sealed record VenmoPaymentTokenUsageType : OpenStringEnum<VenmoPaymentTokenUsageType>
{
    private VenmoPaymentTokenUsageType(string value) : base(value)
    {
    }

    /// <summary>
    /// The Venmo Payment Token will be used for future transaction directly with a merchant.
    /// </summary>
    public static readonly VenmoPaymentTokenUsageType Merchant = new("MERCHANT");

    /// <summary>
    /// The Venmo Payment Token will be used for future transaction on a platform. A platform is typically a marketplace or a channel that a payer can purchase goods and services from multiple merchants.
    /// </summary>
    public static readonly VenmoPaymentTokenUsageType Platform = new("PLATFORM");

    public TResult Match<TResult>(Func<TResult> onMerchant,
        Func<TResult> onPlatform,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Merchant => onMerchant(),
            _ when this == Platform => onPlatform(),
            _ => otherwise(Value)
        };

    public void Match(Action onMerchant, Action onPlatform, Action<string> otherwise)
    {
        if (this == Merchant) onMerchant();
        else if (this == Platform) onPlatform();
        else otherwise(Value);
    }
}
