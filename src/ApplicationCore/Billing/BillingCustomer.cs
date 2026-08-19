namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class BillingCustomer
{
    public int Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? Reference { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
}
