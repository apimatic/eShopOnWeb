namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The details required to create a billing-provider customer for an eShopOnWeb user.
/// </summary>
public class NewBillingCustomer
{
    /// <summary>
    /// The stable eShopOnWeb user reference (email/username) used for idempotent lookup.
    /// </summary>
    public string Reference { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;
}
