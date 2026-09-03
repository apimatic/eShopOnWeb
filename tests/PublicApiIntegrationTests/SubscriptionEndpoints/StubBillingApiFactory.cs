using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

internal sealed class StubBillingApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISubscriptionBillingService>();
            services.AddSingleton<ISubscriptionBillingService, StubSubscriptionBillingService>();
        });
    }
}

internal sealed class StubSubscriptionBillingService : ISubscriptionBillingService
{
    private readonly List<ShopperSubscription> _subscriptions = new();

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SubscriptionPlan> plans =
        [
            new(1, "eshop-pro", "Pro Plan", "Pro", 299.00m, 1, "month"),
            new(2, "basic-plan", "Basic Plan", "Basic", 29.00m, 1, "month")
        ];
        return Task.FromResult(plans);
    }

    public Task<SubscribeResult> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken)
    {
        var existing = _subscriptions.FirstOrDefault(s =>
            string.Equals(s.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return Task.FromResult(new SubscribeResult(existing, false));
        }

        var created = new ShopperSubscription(
            99,
            productHandle,
            "Pro Plan",
            299.00m,
            "active",
            new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
        _subscriptions.Add(created);
        return Task.FromResult(new SubscribeResult(created, true));
    }

    public Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        IReadOnlyList<ShopperSubscription> copy = _subscriptions.ToList();
        return Task.FromResult(copy);
    }
}
