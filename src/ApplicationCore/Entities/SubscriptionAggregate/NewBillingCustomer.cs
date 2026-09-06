namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>Billing customer to create. Maxio rejects the request if any of these are blank.</summary>
public class NewBillingCustomer
{
    public required string Reference { get; init; }

    public required string Email { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }
}
