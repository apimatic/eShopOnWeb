namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class ShopperIdentity
{
    public string UserId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
}

public class BillingCustomer
{
    public int Id { get; init; }
    public string? Reference { get; init; }
    public string Email { get; init; } = string.Empty;
}

public class CreateBillingCustomer
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
}

public class CreateBillingSubscription
{
    public string ProductHandle { get; init; } = string.Empty;
    public int CustomerId { get; init; }
    public string? Reference { get; init; }
    public string PaymentCollectionMethod { get; init; } = "remittance";
}

public class SubscribeResult
{
    public required ShopperSubscription Subscription { get; init; }
    public bool Created { get; init; }
}
