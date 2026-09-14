using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// In-memory stand-in for Maxio used by integration tests, so they exercise the real HTTP
/// pipeline (auth, routing, request/response mapping) without depending on network access or
/// live sandbox credentials. Mirrors the seeded sandbox catalog described in PHASE-BUILD.md.
/// </summary>
public class FakeMaxioBillingService : IMaxioBillingService
{
    public static readonly IReadOnlyList<SubscriptionPlanDto> SeededPlans = new List<SubscriptionPlanDto>
    {
        new() { ProductId = 1, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month", RequiresPaymentMethod = false },
        new() { ProductId = 2, Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month", RequiresPaymentMethod = false },
    };

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SubscriptionDto>> _subscriptionsByUser = new();
    private int _nextSubscriptionId = 1000;
    public int CreateCallCount;

    public Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(SeededPlans);

    public Task<SubscriptionDto> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var plan = SeededPlans.FirstOrDefault(p => p.Handle == request.PlanHandle);
        if (plan is null)
        {
            throw new PlanNotFoundException(request.PlanHandle);
        }

        var userSubscriptions = _subscriptionsByUser.GetOrAdd(request.UserReference, _ => new ConcurrentDictionary<string, SubscriptionDto>());

        var subscription = userSubscriptions.GetOrAdd(request.PlanHandle, _ =>
        {
            Interlocked.Increment(ref CreateCallCount);
            var now = DateTimeOffset.UtcNow;
            return new SubscriptionDto
            {
                SubscriptionId = Interlocked.Increment(ref _nextSubscriptionId),
                State = "active",
                PlanHandle = plan.Handle,
                PlanName = plan.Name,
                PriceInCents = plan.PriceInCents,
                CurrentPeriodEndsAt = now.AddMonths(1),
                NextAssessmentAt = now.AddMonths(1),
                CreatedAt = now
            };
        });

        return Task.FromResult(subscription);
    }

    public Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SubscriptionDto> result = _subscriptionsByUser.TryGetValue(userReference, out var userSubscriptions)
            ? userSubscriptions.Values.ToList()
            : Array.Empty<SubscriptionDto>();

        return Task.FromResult(result);
    }
}
