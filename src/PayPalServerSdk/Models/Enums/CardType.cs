using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Type of card. i.e Credit, Debit and so on.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CardType>))]
public sealed record CardType : OpenStringEnum<CardType>
{
    private CardType(string value) : base(value)
    {
    }

    /// <summary>
    /// A credit card.
    /// </summary>
    public static readonly CardType Credit = new("CREDIT");

    /// <summary>
    /// A debit card.
    /// </summary>
    public static readonly CardType Debit = new("DEBIT");

    /// <summary>
    /// A Prepaid card.
    /// </summary>
    public static readonly CardType Prepaid = new("PREPAID");

    /// <summary>
    /// A store card.
    /// </summary>
    public static readonly CardType Store = new("STORE");

    /// <summary>
    /// Card type cannot be determined.
    /// </summary>
    public static readonly CardType Unknown = new("UNKNOWN");

    public TResult Match<TResult>(Func<TResult> onCredit,
        Func<TResult> onDebit,
        Func<TResult> onPrepaid,
        Func<TResult> onStore,
        Func<TResult> onUnknown,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Credit => onCredit(),
            _ when this == Debit => onDebit(),
            _ when this == Prepaid => onPrepaid(),
            _ when this == Store => onStore(),
            _ when this == Unknown => onUnknown(),
            _ => otherwise(Value)
        };

    public void Match(Action onCredit,
        Action onDebit,
        Action onPrepaid,
        Action onStore,
        Action onUnknown,
        Action<string> otherwise)
    {
        if (this == Credit) onCredit();
        else if (this == Debit) onDebit();
        else if (this == Prepaid) onPrepaid();
        else if (this == Store) onStore();
        else if (this == Unknown) onUnknown();
        else otherwise(Value);
    }
}
