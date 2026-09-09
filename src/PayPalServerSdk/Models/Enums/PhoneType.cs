using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The phone type.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PhoneType>))]
public sealed record PhoneType : StringEnum<PhoneType>
{
    private PhoneType(string value) : base(value)
    {
    }

    /// <summary>
    /// Fax number.
    /// </summary>
    public static readonly PhoneType Fax = new("FAX");

    /// <summary>
    /// Home phone number.
    /// </summary>
    public static readonly PhoneType Home = new("HOME");

    /// <summary>
    /// Mobile phone number.
    /// </summary>
    public static readonly PhoneType Mobile = new("MOBILE");

    /// <summary>
    /// Other phone number.
    /// </summary>
    public static readonly PhoneType Other = new("OTHER");

    /// <summary>
    /// Pager number.
    /// </summary>
    public static readonly PhoneType Pager = new("PAGER");

    public static PhoneType FromValue(string value) => FromValueCore(value);
}
