using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(
        SubscriptionUser user,
        string productHandle,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken);
}

public sealed record SubscriptionUser(
    string UserId,
    string Email,
    string FirstName,
    string LastName);

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record SubscriptionSummary(
    string Reference,
    string ProductHandle,
    string PlanName,
    long PriceInCents,
    string? Currency,
    string State,
    System.DateTimeOffset? NextBillingDate);

public sealed record SubscribeResult(
    SubscriptionSummary? Subscription,
    bool IsPending,
    string? StatusCode)
{
    public static SubscribeResult Completed(SubscriptionSummary subscription) =>
        new(subscription, false, null);

    public static SubscribeResult Pending() =>
        new(null, true, "subscription_pending_reconciliation");
}
