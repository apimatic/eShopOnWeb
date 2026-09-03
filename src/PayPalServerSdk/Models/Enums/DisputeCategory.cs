using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The condition that is covered for the transaction.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<DisputeCategory>))]
public sealed record DisputeCategory : StringEnum<DisputeCategory>
{
    private DisputeCategory(string value) : base(value)
    {
    }

    /// <summary>
    /// The payer paid for an item that they did not receive.
    /// </summary>
    public static readonly DisputeCategory ItemNotReceived = new("ITEM_NOT_RECEIVED");

    /// <summary>
    /// The payer did not authorize the payment.
    /// </summary>
    public static readonly DisputeCategory UnauthorizedTransaction = new("UNAUTHORIZED_TRANSACTION");

    public static DisputeCategory FromValue(string value) => FromValueCore(value);
}
