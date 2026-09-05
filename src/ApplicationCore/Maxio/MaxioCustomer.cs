namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A Maxio Advanced Billing customer.
/// </summary>
public class MaxioCustomer
{
    public long Id { get; init; }
    public string? Reference { get; init; }
    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}
