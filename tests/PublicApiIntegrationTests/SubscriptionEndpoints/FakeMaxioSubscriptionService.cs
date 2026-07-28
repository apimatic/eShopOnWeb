using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Hand-rolled fake of <see cref="IMaxioSubscriptionService"/> so the endpoint behavior can
/// be tested without touching Maxio or the network. Models the idempotency contract: a
/// second subscribe to a live plan returns the existing subscription with AlreadyExisted=true.
/// </summary>
internal sealed class FakeMaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly List<SubscriptionPlan> _plans;
    private readonly List<CustomerSubscription> _subscriptions = new();
    private int _nextId = 1000;

    public FakeMaxioSubscriptionService(IEnumerable<SubscriptionPlan>? plans = null)
    {
        _plans = (plans ?? new[]
        {
            new SubscriptionPlan(1, "basic-plan", "Basic Plan", "cheap", 29m, "USD", "month", "eshop-subscribe"),
            new SubscriptionPlan(2, "eshop-pro", "Pro Plan", null, 299m, "USD", "month", "eshop-subscribe"),
        }).ToList();
    }

    public SubscribeCommand? LastCommand { get; private set; }

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<SubscriptionPlan>>(_plans);

    public Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        LastCommand = command;

        var handle = string.IsNullOrWhiteSpace(command.PlanHandle) ? _plans.First().Handle : command.PlanHandle;
        var existing = _subscriptions.FirstOrDefault(s =>
            string.Equals(s.PlanHandle, handle, StringComparison.OrdinalIgnoreCase) && s.State == "active");

        if (existing != null)
        {
            return Task.FromResult(new SubscribeResult(existing, AlreadyExisted: true));
        }

        var plan = _plans.First(p => p.Handle == handle);
        var created = new CustomerSubscription(
            Id: _nextId++,
            State: "active",
            PlanName: plan.Name,
            PlanHandle: plan.Handle,
            Price: plan.Price,
            Currency: plan.Currency,
            Interval: plan.Interval,
            CurrentPeriodStartsAt: DateTimeOffset.UtcNow,
            CurrentPeriodEndsAt: DateTimeOffset.UtcNow.AddMonths(1),
            NextBillingDate: DateTimeOffset.UtcNow.AddMonths(1),
            CustomerId: 42,
            CustomerReference: command.Subscriber.UserId);
        _subscriptions.Add(created);
        return Task.FromResult(new SubscribeResult(created, AlreadyExisted: false));
    }

    public Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CustomerSubscription>>(_subscriptions.ToList());
}
