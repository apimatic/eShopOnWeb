using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record BillingPlan(
    int? Id,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record BillingCustomer(int Id, string Reference);

public sealed record BillingSubscription(
    int Id,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CurrentPeriodEndsAt,
    string? Currency,
    string? Reference);

public interface IMaxioBillingGateway
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<BillingCustomer> EnsureCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken);
    Task<BillingSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<BillingSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string reference, CancellationToken cancellationToken);
    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDto> SubscribeAsync(string applicationUserId, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListForUserAsync(string applicationUserId, CancellationToken cancellationToken);
}
