using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public sealed record BillingUser(string Id, string Email, string FirstName, string LastName);

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit);

public sealed record SubscriptionDetails(
    string Reference,
    string PlanHandle,
    string PlanName,
    long? PriceInCents,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingAt,
    bool IsPending);

public sealed record SubscriptionReservation(RecurringSubscription Subscription, bool Created);

public interface IRecurringSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDetails> SubscribeAsync(BillingUser user, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDetails>> ListForUserAsync(string applicationUserId, CancellationToken cancellationToken);
}

public interface ISubscriptionBillingStore
{
    Task<MaxioCustomerMapping?> FindCustomerAsync(string applicationUserId, CancellationToken cancellationToken);
    Task<MaxioCustomerMapping> GetOrCreateCustomerAsync(string applicationUserId, string maxioReference, CancellationToken cancellationToken);
    Task SaveCustomerAsync(MaxioCustomerMapping mapping, CancellationToken cancellationToken);
    Task<RecurringSubscription?> FindSubscriptionAsync(string applicationUserId, string productHandle, CancellationToken cancellationToken);
    Task<SubscriptionReservation> GetOrCreateSubscriptionAsync(RecurringSubscription subscription, CancellationToken cancellationToken);
    Task<IReadOnlyList<RecurringSubscription>> ListSubscriptionsAsync(string applicationUserId, CancellationToken cancellationToken);
    Task SaveSubscriptionAsync(RecurringSubscription subscription, CancellationToken cancellationToken);
}

public interface ISubscriptionOperationLock
{
    ValueTask<IAsyncDisposable> AcquireAsync(string applicationUserId, string productHandle, CancellationToken cancellationToken);
}
