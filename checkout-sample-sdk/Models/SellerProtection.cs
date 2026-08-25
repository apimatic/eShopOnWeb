using System.Collections.Generic;
using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The level of protection offered as defined by <see href="https://www.paypal.com/us/webapps/mpp/security/seller-protection">PayPal Seller Protection for Merchants</see>.
/// </summary>
public record SellerProtection
{
    /// <summary>
    /// Indicates whether the transaction is eligible for seller protection. For information, see <see href="https://www.paypal.com/us/webapps/mpp/security/seller-protection">PayPal Seller Protection for Merchants</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public SellerProtectionStatus? Status { get; init; }

    /// <summary>
    /// An array of conditions that are covered for the transaction.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("dispute_categories")]
    public IReadOnlyList<DisputeCategory>? DisputeCategories { get; init; }
}
