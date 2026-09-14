namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The billing system's record of an eShopOnWeb shopper.
/// <see cref="Reference"/> is the deterministic key this application owns and looks customers
/// up by, so the shopper-to-customer mapping survives without any local persistence.
/// </summary>
public record BillingCustomer
{
    public required long Id { get; init; }
    public string? Reference { get; init; }
    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}
