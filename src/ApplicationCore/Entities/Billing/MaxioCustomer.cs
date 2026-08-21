namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

/// <summary>
/// A customer record in Maxio Advanced Billing, keyed to an eShopOnWeb user by <see cref="Reference"/>.
/// </summary>
public sealed class MaxioCustomer
{
    public int Id { get; init; }
    public string? Reference { get; init; }
    public string Email { get; init; } = string.Empty;
}
