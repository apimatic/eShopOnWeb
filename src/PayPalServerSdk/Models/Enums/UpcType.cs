using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The Universal Product Code type.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<UpcType>))]
public sealed record UpcType : StringEnum<UpcType>
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

    public static UpcType FromValue(string value) => FromValueCore(value);
}
