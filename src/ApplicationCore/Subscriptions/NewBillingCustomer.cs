namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>Instruction to create a Maxio customer for an eShopOnWeb shopper.</summary>
public sealed class NewBillingCustomer
{
    public required string Reference { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
}
