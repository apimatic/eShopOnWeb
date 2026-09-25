using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// CallBack event.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CallbackEvents>))]
public sealed record CallbackEvents : OpenStringEnum<CallbackEvents>
{
    private CallbackEvents(string value) : base(value)
    {
    }

    /// <summary>
    /// When Buyer changes or selects the shipping address on the PayPal/Venmo buyer approval flow , PayPal/Venmo will call merchant with the callback URL to update order totals.
    /// </summary>
    public static readonly CallbackEvents ShippingAddress = new("SHIPPING_ADDRESS");

    /// <summary>
    /// When Buyer changes or selects the shipping options on the PayPal/Venmo buyer approval flow , PayPal/Venmo will call merchant with the callback URL to update order totals.
    /// </summary>
    public static readonly CallbackEvents ShippingOptions = new("SHIPPING_OPTIONS");

    public TResult Match<TResult>(Func<TResult> onShippingAddress,
        Func<TResult> onShippingOptions,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == ShippingAddress => onShippingAddress(),
            _ when this == ShippingOptions => onShippingOptions(),
            _ => otherwise(Value)
        };

    public void Match(Action onShippingAddress, Action onShippingOptions, Action<string> otherwise)
    {
        if (this == ShippingAddress) onShippingAddress();
        else if (this == ShippingOptions) onShippingOptions();
        else otherwise(Value);
    }
}
