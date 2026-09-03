using System.Text.Json.Serialization;
using PayPal.Core.Enum;

namespace PayPal.Models.Enums;

/// <summary>
/// The usage type associated with the PayPal payment token.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<UsageType>))]
public sealed record UsageType : StringEnum<UsageType>
{
    private UsageType(string value) : base(value)
    {
    }

    /// <summary>
    /// The PayPal Payment Token will be used for future transaction directly with a merchant.
    /// </summary>
    public static readonly UsageType Merchant = new("MERCHANT");

    /// <summary>
    /// The PayPal Payment Token will be used for future transaction on a platform. A platform is typically a marketplace or a channel that a payer can purchase goods and services from multiple merchants.
    /// </summary>
    public static readonly UsageType Platform = new("PLATFORM");

    public static UsageType FromValue(string value) => FromValueCore(value);
}
