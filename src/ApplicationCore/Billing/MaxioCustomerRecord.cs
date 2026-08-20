namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class MaxioCustomerRecord
{
    public int Id { get; init; }
    public string? Email { get; init; }
    public string? Reference { get; init; }
}
