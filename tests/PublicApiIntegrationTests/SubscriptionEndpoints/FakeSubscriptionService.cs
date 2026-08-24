using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Canned ISubscriptionService so endpoint tests never touch the real billing provider.
/// </summary>
public class FakeSubscriptionService : ISubscriptionService
{
    public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SubscriptionPlanDto> plans = new List<SubscriptionPlanDto>
        {
            new SubscriptionPlanDto { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
            new SubscriptionPlanDto { Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" }
        };
        return Task.FromResult(plans);
    }

    public Task<SubscriptionDto> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new SubscriptionDto
        {
            Id = 42,
            State = "active",
            ProductHandle = command.ProductHandle,
            ProductName = "Pro Plan",
            PriceInCents = 29900,
            NextBillingDate = DateTimeOffset.UtcNow.AddMonths(1)
        });
    }

    public Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SubscriptionDto> subscriptions = new List<SubscriptionDto>
        {
            new SubscriptionDto
            {
                Id = 42,
                State = "active",
                ProductHandle = "eshop-pro",
                ProductName = "Pro Plan",
                PriceInCents = 29900,
                NextBillingDate = DateTimeOffset.UtcNow.AddMonths(1)
            }
        };
        return Task.FromResult(subscriptions);
    }
}
