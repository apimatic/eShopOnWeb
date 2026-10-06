namespace Microsoft.eShopWeb.ApplicationCore.Billing.Models;

/// <summary>
/// Data for ensuring a Maxio customer exists for an eShopOnWeb user.
/// <see cref="Reference"/> must be the stable, unique eShopOnWeb user identifier.
/// </summary>
public record NewBillingCustomer
{
    public required string Reference { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
}
