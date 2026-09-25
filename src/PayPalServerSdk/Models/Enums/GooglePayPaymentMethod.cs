using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The type of the payment credential. Currently, only CARD is supported.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<GooglePayPaymentMethod>))]
public sealed record GooglePayPaymentMethod : OpenStringEnum<GooglePayPaymentMethod>
{
    private GooglePayPaymentMethod(string value) : base(value)
    {
    }

    /// <summary>
    /// CARD is the only value that Google Pay accepts.
    /// </summary>
    public static readonly GooglePayPaymentMethod Card = new("CARD");

    public TResult Match<TResult>(Func<TResult> onCard, Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Card => onCard(),
            _ => otherwise(Value)
        };

    public void Match(Action onCard, Action<string> otherwise)
    {
        if (this == Card) onCard();
        else otherwise(Value);
    }
}
