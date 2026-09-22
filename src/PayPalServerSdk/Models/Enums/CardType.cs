using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Type of card. i.e Credit, Debit and so on.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CardType>))]
public sealed record CardType : StringEnum<CardType>
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

    public static CardType FromValue(string value) => FromValueCore(value);
}
