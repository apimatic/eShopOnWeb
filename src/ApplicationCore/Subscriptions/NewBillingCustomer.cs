namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The details used to create a billing customer for an eShopOnWeb user.
/// </summary>
public class NewBillingCustomer
{
    /// <summary>The eShopOnWeb-owned identifier for this customer. Must be unique per Maxio site.</summary>
    public string Reference { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Organization { get; init; }
}
