using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

namespace PublicApiIntegrationTests;

public sealed class TestMaxioBillingGateway : IMaxioBillingGateway
{
    private readonly ConcurrentDictionary<string, SubscriptionDto> _subscriptions = new(StringComparer.Ordinal);
    private int _createCalls;

    public int CreateCalls => Volatile.Read(ref _createCalls);

    public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SubscriptionPlanDto>>([Plan()]);

    public Task<SubscriptionPlanDto> GetPlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        if (!string.Equals(productHandle, "test-plan", StringComparison.Ordinal))
        {
            throw new BillingProviderException(
                BillingProviderFailureKind.Rejected,
                "The requested subscription plan is not available.",
                HttpStatusCode.NotFound);
        }

        return Task.FromResult(Plan());
    }

    public Task<BillingCustomer> EnsureCustomerAsync(
        string customerReference,
        BillingCustomerProfile profile,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BillingCustomer(42, customerReference));

    public Task<SubscriptionDto?> FindSubscriptionAsync(
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        _subscriptions.TryGetValue(subscriptionReference, out var subscription);
        return Task.FromResult(subscription);
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _createCalls);
        await Task.Delay(100, cancellationToken);
        var subscription = new SubscriptionDto(
            99,
            subscriptionReference,
            productHandle,
            "Test Plan",
            29900,
            299m,
            1,
            "month",
            "active",
            DateTimeOffset.UtcNow.AddMonths(1));
        _subscriptions[subscriptionReference] = subscription;
        return subscription;
    }

    public Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(
        string customerReference,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SubscriptionDto>>(_subscriptions.Values.ToArray());

    private static SubscriptionPlanDto Plan() =>
        new("test-plan", "Test Plan", "A test plan", 29900, 299m, 1, "month", null, null);
}
