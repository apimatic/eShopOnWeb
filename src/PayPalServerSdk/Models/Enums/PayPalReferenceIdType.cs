using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The PayPal reference ID type.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PayPalReferenceIdType>))]
public sealed record PayPalReferenceIdType : StringEnum<PayPalReferenceIdType>
{
    private PayPalReferenceIdType(string value) : base(value)
    {
    }

    /// <summary>
    /// An order ID.
    /// </summary>
    public static readonly PayPalReferenceIdType Odr = new("ODR");

    /// <summary>
    /// A transaction ID.
    /// </summary>
    public static readonly PayPalReferenceIdType Txn = new("TXN");

    /// <summary>
    /// A subscription ID.
    /// </summary>
    public static readonly PayPalReferenceIdType Sub = new("SUB");

    /// <summary>
    /// A pre-approved payment ID.
    /// </summary>
    public static readonly PayPalReferenceIdType Pap = new("PAP");

    public static PayPalReferenceIdType FromValue(string value) => FromValueCore(value);
}
