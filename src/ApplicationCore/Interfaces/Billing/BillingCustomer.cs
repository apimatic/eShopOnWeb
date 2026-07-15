namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

/// <summary>
/// The billing-provider customer record linked to an eShopOnWeb user, keyed on the stable
/// user reference (see <see cref="IBillingClient.EnsureCustomerAsync"/>).
/// </summary>
public sealed record BillingCustomer
{
    public required int Id { get; init; }
    public required string Reference { get; init; }
    public required string Email { get; init; }
}
