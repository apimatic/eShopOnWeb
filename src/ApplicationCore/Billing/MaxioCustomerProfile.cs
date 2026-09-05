namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The eShopOnWeb identity information needed to find-or-create the matching
/// Maxio customer. <see cref="Reference"/> is the eShopOnWeb user name, which
/// Maxio stores as the customer's unique `reference` and is what makes
/// customer provisioning idempotent.
/// </summary>
public class MaxioCustomerProfile
{
    public required string Reference { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
}
