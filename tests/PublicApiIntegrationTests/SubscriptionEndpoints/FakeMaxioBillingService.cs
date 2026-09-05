using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Maxio;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Stand-in for the real Maxio HTTP client so these endpoint tests exercise routing,
/// authentication and request/response mapping without calling out to Maxio. The
/// idempotency/customer-and-subscription logic itself is covered by
/// UnitTests.Infrastructure.Maxio.MaxioBillingServiceTests against a faked HTTP layer.
/// </summary>
public class FakeMaxioBillingService : IMaxioBillingService
{
    public List<SubscriptionPlan> Plans { get; set; } = new();
    public SubscriptionEnrollment? SubscribeResult { get; set; }
    public List<SubscriptionEnrollment> Subscriptions { get; set; } = new();

    public string? LastSubscribeBuyerId { get; private set; }
    public string? LastSubscribeProductHandle { get; private set; }
    public string? LastGetSubscriptionsBuyerId { get; private set; }
    public int SubscribeCallCount { get; private set; }

    public Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SubscriptionPlan>>(Plans);

    public Task<SubscriptionEnrollment> SubscribeAsync(string buyerId, string buyerEmail, string productHandle, CancellationToken cancellationToken = default)
    {
        SubscribeCallCount++;
        LastSubscribeBuyerId = buyerId;
        LastSubscribeProductHandle = productHandle;
        return Task.FromResult(SubscribeResult ?? throw new InvalidOperationException("SubscribeResult not configured for this test."));
    }

    public Task<IReadOnlyList<SubscriptionEnrollment>> GetSubscriptionsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        LastGetSubscriptionsBuyerId = buyerId;
        return Task.FromResult<IReadOnlyList<SubscriptionEnrollment>>(Subscriptions);
    }
}
