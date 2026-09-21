using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The customer's tax ID type.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<TaxIdType>))]
public sealed record TaxIdType : StringEnum<TaxIdType>
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

    public static TaxIdType FromValue(string value) => FromValueCore(value);
}
