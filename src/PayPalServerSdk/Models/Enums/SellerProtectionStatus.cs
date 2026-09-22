using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Indicates whether the transaction is eligible for seller protection. For information, see <see href="https://www.paypal.com/us/webapps/mpp/security/seller-protection">PayPal Seller Protection for Merchants</see>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SellerProtectionStatus>))]
public sealed record SellerProtectionStatus : StringEnum<SellerProtectionStatus>
{
    private SellerProtectionStatus(string value) : base(value)
    {
    }

    /// <summary>
    /// Your PayPal balance remains intact if the customer claims that they did not receive an item or the account holder claims that they did not authorize the payment.
    /// </summary>
    public static readonly SellerProtectionStatus Eligible = new("ELIGIBLE");

    /// <summary>
    /// Your PayPal balance remains intact if the customer claims that they did not receive an item.
    /// </summary>
    public static readonly SellerProtectionStatus PartiallyEligible = new("PARTIALLY_ELIGIBLE");

    /// <summary>
    /// This transaction is not eligible for seller protection.
    /// </summary>
    public static readonly SellerProtectionStatus NotEligible = new("NOT_ELIGIBLE");

    public static SellerProtectionStatus FromValue(string value) => FromValueCore(value);
}
