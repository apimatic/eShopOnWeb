namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class CreateBillingSubscription
{
    public string ProductHandle { get; init; } = string.Empty;
    public int CustomerId { get; init; }
    public string Reference { get; init; } = string.Empty;
}
