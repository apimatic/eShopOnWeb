namespace Microsoft.eShopWeb.ApplicationCore.Billing.Contracts;

public sealed record CreateCustomerCommand(
    string Reference,
    string FirstName,
    string LastName,
    string Email);

public sealed record CreateSubscriptionCommand(
    string ProductHandle,
    long CustomerId,
    string? PaymentCollectionMethod = null);

public sealed record SubscribeCommand(
    string UserKey,
    string PlanHandle,
    string? FirstName = null,
    string? LastName = null,
    string? Email = null);
