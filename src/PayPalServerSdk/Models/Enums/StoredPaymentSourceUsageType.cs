using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Indicates if this is a <c>first</c> or <c>subsequent</c> payment using a stored payment source (also referred to as stored credential or card on file).
/// </summary>
[JsonConverter(typeof(StringEnumConverter<StoredPaymentSourceUsageType>))]
public sealed record StoredPaymentSourceUsageType : StringEnum<StoredPaymentSourceUsageType>
{
    private StoredPaymentSourceUsageType(string value) : base(value)
    {
    }

    /// <summary>
    /// Indicates the Initial/First payment with a payment_source that is intended to be stored upon successful processing of the payment.
    /// </summary>
    public static readonly StoredPaymentSourceUsageType First = new("FIRST");

    /// <summary>
    /// Indicates a payment using a stored payment_source which has been successfully used previously for a payment.
    /// </summary>
    public static readonly StoredPaymentSourceUsageType Subsequent = new("SUBSEQUENT");

    /// <summary>
    /// Indicates that PayPal will derive the value of <c>FIRST</c> or <c>SUBSEQUENT</c> based on data available to PayPal.
    /// </summary>
    public static readonly StoredPaymentSourceUsageType Derived = new("DERIVED");

    public static StoredPaymentSourceUsageType FromValue(string value) => FromValueCore(value);
}
