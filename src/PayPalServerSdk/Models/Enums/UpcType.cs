using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The Universal Product Code type.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<UpcType>))]
public sealed record UpcType : OpenStringEnum<UpcType>
{
    private UpcType(string value) : base(value)
    {
    }

    public static readonly UpcType UpcA = new("UPC-A");

    public static readonly UpcType UpcB = new("UPC-B");

    public static readonly UpcType UpcC = new("UPC-C");

    public static readonly UpcType UpcD = new("UPC-D");

    public static readonly UpcType UpcE = new("UPC-E");

    public static readonly UpcType Upc2 = new("UPC-2");

    public static readonly UpcType Upc5 = new("UPC-5");

    public TResult Match<TResult>(Func<TResult> onUpcA,
        Func<TResult> onUpcB,
        Func<TResult> onUpcC,
        Func<TResult> onUpcD,
        Func<TResult> onUpcE,
        Func<TResult> onUpc2,
        Func<TResult> onUpc5,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == UpcA => onUpcA(),
            _ when this == UpcB => onUpcB(),
            _ when this == UpcC => onUpcC(),
            _ when this == UpcD => onUpcD(),
            _ when this == UpcE => onUpcE(),
            _ when this == Upc2 => onUpc2(),
            _ when this == Upc5 => onUpc5(),
            _ => otherwise(Value)
        };

    public void Match(Action onUpcA,
        Action onUpcB,
        Action onUpcC,
        Action onUpcD,
        Action onUpcE,
        Action onUpc2,
        Action onUpc5,
        Action<string> otherwise)
    {
        if (this == UpcA) onUpcA();
        else if (this == UpcB) onUpcB();
        else if (this == UpcC) onUpcC();
        else if (this == UpcD) onUpcD();
        else if (this == UpcE) onUpcE();
        else if (this == Upc2) onUpc2();
        else if (this == Upc5) onUpc5();
        else otherwise(Value);
    }
}
