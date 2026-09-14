namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// Identity attributes used to provision (and later reference) a Maxio customer for an
/// eShopOnWeb shopper.
/// </summary>
public class SubscriptionCustomer
{
    /// <summary>
    /// Unique application-owned identifier stored as the Maxio customer <c>reference</c>.
    /// </summary>
    public string Reference { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}
