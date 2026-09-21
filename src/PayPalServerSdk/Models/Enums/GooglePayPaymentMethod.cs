using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The type of the payment credential. Currently, only CARD is supported.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<GooglePayPaymentMethod>))]
public sealed record GooglePayPaymentMethod : StringEnum<GooglePayPaymentMethod>
{
    private GooglePayPaymentMethod(string value) : base(value)
    {
    }

    /// <summary>
    /// CARD is the only value that Google Pay accepts.
    /// </summary>
    public static readonly GooglePayPaymentMethod Card = new("CARD");

    public static GooglePayPaymentMethod FromValue(string value) => FromValueCore(value);
}
