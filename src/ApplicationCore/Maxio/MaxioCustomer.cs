namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A customer as represented in Maxio Advanced Billing.
/// </summary>
public class MaxioCustomer
{
    public int Id { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Reference { get; init; }
}
