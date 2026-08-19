namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed record BillingCustomer(int Id, string? Reference, string Email);

public sealed record CreateBillingCustomer(string FirstName, string LastName, string Email, string Reference);

public sealed record CreateBillingSubscription(
    string ProductHandle,
    int CustomerId,
    string Reference,
    string PaymentCollectionMethod);
