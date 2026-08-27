using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public sealed record BillingPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    string Currency,
    int Interval,
    string IntervalUnit);

public sealed record BillingProduct(
    string Handle,
    string Name,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record BillingSubscription(
    int Id,
    string Reference,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    string Currency,
    string State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CurrentPeriodEndsAt);

public sealed record BillingCustomerProfile(
    string FirstName,
    string LastName,
    string Email,
    string Reference);

public sealed record BillingCustomer(int Id, string Reference);

public sealed record SubscriptionResult(BillingSubscription Subscription, bool Created);

public interface IMaxioBillingGateway
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<BillingProduct?> FindProductAsync(string productHandle, CancellationToken cancellationToken);
    Task<BillingSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<BillingCustomer> EnsureCustomerAsync(BillingCustomerProfile profile, CancellationToken cancellationToken);
    Task<BillingSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string subscriptionReference, CancellationToken cancellationToken);
    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(string customerReference, string ownedReferencePrefix, CancellationToken cancellationToken);
    Task CheckHealthAsync(CancellationToken cancellationToken);
}

public interface ISubscriptionService
{
    Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionResult> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<BillingSubscription>> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken);
}
