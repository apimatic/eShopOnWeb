using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Stands in for the real Maxio integration in endpoint-level tests, so the test suite stays
/// hermetic (no network calls to the Maxio sandbox) while still exercising auth, request/response
/// mapping and identity plumbing on the real ASP.NET pipeline.
/// </summary>
public class FakeMaxioSubscriptionService : IMaxioSubscriptionService
{
    public List<(string CustomerReference, string CustomerEmail, string PlanHandle)> SubscribeCalls { get; } = new();

    public Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlansAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SubscriptionPlan>>(new List<SubscriptionPlan>
        {
            new()
            {
                Handle = "eshop-pro",
                Name = "Pro Plan",
                Description = "The pro plan",
                PriceInCents = 29900,
                Interval = 1,
                IntervalUnit = "month"
            }
        });

    public Task<CustomerSubscription> SubscribeAsync(string customerReference, string customerEmail, string planHandle, CancellationToken cancellationToken = default)
    {
        SubscribeCalls.Add((customerReference, customerEmail, planHandle));

        return Task.FromResult(new CustomerSubscription
        {
            SubscriptionId = 42,
            State = "active",
            PlanHandle = planHandle,
            PlanName = "Pro Plan",
            PriceInCents = 29900,
            CurrentPeriodEndsAt = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero),
            NextAssessmentAt = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero),
            ActivatedAt = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero)
        });
    }

    public Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CustomerSubscription>>(new List<CustomerSubscription>
        {
            new()
            {
                SubscriptionId = 42,
                State = "active",
                PlanHandle = "eshop-pro",
                PlanName = "Pro Plan",
                PriceInCents = 29900,
                CreatedAt = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero)
            }
        });
}
