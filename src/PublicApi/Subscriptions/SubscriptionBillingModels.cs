using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record BillingUser(string Id, string UserName, string Email);

public sealed record SubscriptionView(
    long Id,
    string ProductName,
    string ProductHandle,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt);

public sealed record SubscribeResult(SubscriptionView Subscription, bool Created);

public interface ISubscriptionBillingService
{
    System.Threading.Tasks.Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(
        System.Threading.CancellationToken cancellationToken);

    System.Threading.Tasks.Task<SubscribeResult> SubscribeAsync(
        BillingUser user,
        string productHandle,
        System.Threading.CancellationToken cancellationToken);

    System.Threading.Tasks.Task<IReadOnlyList<SubscriptionView>> GetSubscriptionsAsync(
        BillingUser user,
        System.Threading.CancellationToken cancellationToken);
}

public class SubscriptionRequestException : Exception
{
    public SubscriptionRequestException(string message) : base(message) { }
}
