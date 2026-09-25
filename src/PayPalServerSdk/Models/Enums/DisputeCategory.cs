using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The condition that is covered for the transaction.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<DisputeCategory>))]
public sealed record DisputeCategory : OpenStringEnum<DisputeCategory>
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

    public TResult Match<TResult>(Func<TResult> onItemNotReceived,
        Func<TResult> onUnauthorizedTransaction,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == ItemNotReceived => onItemNotReceived(),
            _ when this == UnauthorizedTransaction => onUnauthorizedTransaction(),
            _ => otherwise(Value)
        };

    public void Match(Action onItemNotReceived, Action onUnauthorizedTransaction, Action<string> otherwise)
    {
        if (this == ItemNotReceived) onItemNotReceived();
        else if (this == UnauthorizedTransaction) onUnauthorizedTransaction();
        else otherwise(Value);
    }
}
