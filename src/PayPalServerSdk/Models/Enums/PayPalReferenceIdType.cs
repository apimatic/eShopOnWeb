using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The PayPal reference ID type.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PayPalReferenceIdType>))]
public sealed record PayPalReferenceIdType : OpenStringEnum<PayPalReferenceIdType>
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

    public TResult Match<TResult>(Func<TResult> onOdr,
        Func<TResult> onTxn,
        Func<TResult> onSub,
        Func<TResult> onPap,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Odr => onOdr(),
            _ when this == Txn => onTxn(),
            _ when this == Sub => onSub(),
            _ when this == Pap => onPap(),
            _ => otherwise(Value)
        };

    public void Match(Action onOdr, Action onTxn, Action onSub, Action onPap, Action<string> otherwise)
    {
        if (this == Odr) onOdr();
        else if (this == Txn) onTxn();
        else if (this == Sub) onSub();
        else if (this == Pap) onPap();
        else otherwise(Value);
    }
}
