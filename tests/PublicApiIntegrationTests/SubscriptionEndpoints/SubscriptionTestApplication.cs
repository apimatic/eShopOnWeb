using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Configurable stand-in for the Maxio gateway so endpoint tests never touch the
/// network or require Maxio credentials. Mirrors the idempotency contract of
/// <see cref="MaxioSubscriptionService"/> (one non-terminal subscription per plan).
/// </summary>
public sealed class FakeMaxioSubscriptionService : IMaxioSubscriptionService
{
    public Exception? ExceptionToThrow { get; set; }

    public List<SubscriptionPlanDto> Plans { get; } = new List<SubscriptionPlanDto>
    {
        new SubscriptionPlanDto
        {
            Handle = "eshop-pro",
            Name = "Pro Plan",
            Price = 299.00m,
            Currency = "USD",
            Interval = 1,
            IntervalUnit = "month",
            IsDefault = true,
            PaymentRequired = false
        },
        new SubscriptionPlanDto
        {
            Handle = "basic-plan",
            Name = "Basic Plan",
            Price = 29.00m,
            Currency = "USD",
            Interval = 1,
            IntervalUnit = "month",
            IsDefault = false,
            PaymentRequired = false
        }
    };

    public List<SubscriptionDto> Subscriptions { get; } = new List<SubscriptionDto>();

    public string? LastCustomerReference { get; private set; }

    public string? LastPlanHandle { get; private set; }

    public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct)
    {
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult<IReadOnlyList<SubscriptionPlanDto>>(Plans.ToList());
    }

    public Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string customerReference, CancellationToken ct)
    {
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        LastCustomerReference = customerReference;
        return Task.FromResult<IReadOnlyList<SubscriptionDto>>(Subscriptions.ToList());
    }

    public Task<SubscribeToPlanResult> SubscribeAsync(SubscribeToPlanRequest request, CancellationToken ct)
    {
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        LastCustomerReference = request.CustomerReference;
        LastPlanHandle = request.PlanHandle;

        var existing = Subscriptions.FirstOrDefault(s => s.PlanHandle == request.PlanHandle);
        if (existing is not null)
        {
            return Task.FromResult(new SubscribeToPlanResult { Subscription = existing, Created = false });
        }

        var plan = Plans.First(p => p.Handle == request.PlanHandle);
        var created = new SubscriptionDto
        {
            Id = 700000 + Subscriptions.Count,
            PlanHandle = plan.Handle,
            PlanName = plan.Name,
            Price = plan.Price,
            Currency = plan.Currency,
            State = "active",
            NextBillingDate = DateTimeOffset.UtcNow.AddMonths(1),
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
            IsActive = true
        };
        Subscriptions.Add(created);

        return Task.FromResult(new SubscribeToPlanResult { Subscription = created, Created = true });
    }
}

/// <summary>
/// WebApplicationFactory for the PublicApi host with the Maxio gateway replaced by
/// <see cref="FakeMaxioSubscriptionService"/>.
/// </summary>
public sealed class SubscriptionTestApplication : WebApplicationFactory<Program>
{
    public FakeMaxioSubscriptionService Fake { get; } = new FakeMaxioSubscriptionService();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IMaxioSubscriptionService>(Fake);
        });
    }
}
