using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The customer's tax ID type.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<TaxIdType>))]
public sealed record TaxIdType : OpenStringEnum<TaxIdType>
{
    private TaxIdType(string value) : base(value)
    {
    }

    /// <summary>
    /// The individual tax ID type, typically is 11 characters long.
    /// </summary>
    public static readonly TaxIdType BrCpf = new("BR_CPF");

    /// <summary>
    /// The business tax ID type, typically is 14 characters long.
    /// </summary>
    public static readonly TaxIdType BrCnpj = new("BR_CNPJ");

    public TResult Match<TResult>(Func<TResult> onBrCpf, Func<TResult> onBrCnpj, Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == BrCpf => onBrCpf(),
            _ when this == BrCnpj => onBrCnpj(),
            _ => otherwise(Value)
        };

    public void Match(Action onBrCpf, Action onBrCnpj, Action<string> otherwise)
    {
        if (this == BrCpf) onBrCpf();
        else if (this == BrCnpj) onBrCnpj();
        else otherwise(Value);
    }
}
