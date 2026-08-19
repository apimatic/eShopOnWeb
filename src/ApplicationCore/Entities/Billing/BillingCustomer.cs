namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

public class BillingCustomer
{
    public int Id { get; init; }
    public string? Reference { get; init; }
    public string Email { get; init; } = string.Empty;
}
