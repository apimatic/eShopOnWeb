using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Indicates the type of payment data passed, in case of Non China the payment data is 3DSECURE and for China it is EMV.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ApplePayPaymentDataType>))]
public sealed record ApplePayPaymentDataType : OpenStringEnum<ApplePayPaymentDataType>
{
    private ApplePayPaymentDataType(string value) : base(value)
    {
    }

    /// <summary>
    /// The card was authenticated using 3D Secure (3DS) authentication scheme. While using this value make sure to populate cryptogram and eci_indicator as part of payment data..
    /// </summary>
    public static readonly ApplePayPaymentDataType _3Dsecure = new("3DSECURE");

    /// <summary>
    /// The card was authenticated using EMV method, which is applicable for China. While using this value make sure to pass emv_data and pin as part of payment data.
    /// </summary>
    public static readonly ApplePayPaymentDataType Emv = new("EMV");

    public TResult Match<TResult>(Func<TResult> on_3Dsecure, Func<TResult> onEmv, Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == _3Dsecure => on_3Dsecure(),
            _ when this == Emv => onEmv(),
            _ => otherwise(Value)
        };

    public void Match(Action on_3Dsecure, Action onEmv, Action<string> otherwise)
    {
        if (this == _3Dsecure) on_3Dsecure();
        else if (this == Emv) onEmv();
        else otherwise(Value);
    }
}
