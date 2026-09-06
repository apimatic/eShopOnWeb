namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A customer record in the billing system, linked back to an eShopOnWeb user through
/// <see cref="Reference"/>.
/// </summary>
public class BillingCustomer
{
    public int Id { get; init; }
    public string? Reference { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Organization { get; init; }
}
