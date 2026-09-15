namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class MaxioCustomer
{
    public int Id { get; init; }
    public string? Reference { get; init; }
    public string? Email { get; init; }
}
