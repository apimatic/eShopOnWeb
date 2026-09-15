namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class BillingCustomer
{
    public int Id { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}
