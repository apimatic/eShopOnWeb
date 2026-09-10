using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Indicates the type of payment data passed, in case of Non China the payment data is 3DSECURE and for China it is EMV.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ApplePayPaymentDataType>))]
public sealed record ApplePayPaymentDataType : StringEnum<ApplePayPaymentDataType>
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

    public static ApplePayPaymentDataType FromValue(string value) => FromValueCore(value);
}
