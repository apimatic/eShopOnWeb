namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class CreateBillingCustomer
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
}
