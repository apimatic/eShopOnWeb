namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>Attributes used to create a billing customer.</summary>
public sealed record NewBillingCustomer
{
    public required string Reference { get; init; }

    public required string Email { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }
}
