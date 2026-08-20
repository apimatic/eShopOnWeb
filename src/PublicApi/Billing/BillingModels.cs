using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public sealed record BillingCustomer(
    string Subject,
    string FirstName,
    string LastName,
    string Email)
{
    public string MaxioReference => $"eshop-user:{Subject}";
}

public sealed record SubscriptionPlan(
    string ProductHandle,
    string Name,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record UserSubscription(
    string Reference,
    string ProductHandle,
    string ProductName,
    long? PriceInCents,
    string State,
    DateTimeOffset? NextBillingAt);
