namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

public sealed class MaxioCustomer
{
    public int Id { get; init; }
    public string? Reference { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
}
