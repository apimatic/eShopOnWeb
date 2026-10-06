namespace Microsoft.eShopWeb.ApplicationCore.Models.SubscriptionBilling;

/// <summary>
/// Identifies the eShopOnWeb user for billing-system customer provisioning.
/// The user id is carried as the billing customer reference so the mapping
/// is stable and idempotent.
/// </summary>
public class BillingCustomerInfo
{
    /// <summary>
    /// The eShopOnWeb user id (used as the customer reference in the billing system).
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;
}