using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Authentication Method which is used for the card transaction.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<GooglePayAuthenticationMethod>))]
public sealed record GooglePayAuthenticationMethod : OpenStringEnum<GooglePayAuthenticationMethod>
{
    private GooglePayAuthenticationMethod(string value) : base(value)
    {
    }

    /// <summary>
    /// This authentication method is associated with payment cards stored on file with the user's Google Account. Returned payment data includes primary account number (PAN) with the expiration month and the expiration year.
    /// </summary>
    public static readonly GooglePayAuthenticationMethod PanOnly = new("PAN_ONLY");

    /// <summary>
    /// Returned payment data includes a 3-D Secure (3DS) cryptogram generated on the device. -&gt; If authentication_method=CRYPTOGRAM, it is required that 'cryptogram' parameter in the request has a valid 3-D Secure (3DS) cryptogram generated on the device.
    /// </summary>
    public static readonly GooglePayAuthenticationMethod Cryptogram3Ds = new("CRYPTOGRAM_3DS");

    public TResult Match<TResult>(Func<TResult> onPanOnly,
        Func<TResult> onCryptogram3Ds,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == PanOnly => onPanOnly(),
            _ when this == Cryptogram3Ds => onCryptogram3Ds(),
            _ => otherwise(Value)
        };

    public void Match(Action onPanOnly, Action onCryptogram3Ds, Action<string> otherwise)
    {
        if (this == PanOnly) onPanOnly();
        else if (this == Cryptogram3Ds) onCryptogram3Ds();
        else otherwise(Value);
    }
}
