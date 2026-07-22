using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// A scriptable stand-in for the provider seam, used to test the orchestration above it without
/// re-testing the wire format.
/// </summary>
/// <remarks>
/// It records what it was asked to do so tests can assert the negative cases that matter most:
/// that an illegal transition or a rejected input never reaches the provider at all.
/// </remarks>
internal sealed class FakeBillingClient : IBillingClient
{
    internal List<BillingPlan> Plans { get; } = new();

    internal BillingCustomer? Customer { get; set; }

    internal List<Subscription> Subscriptions { get; } = new();

    internal MeteredComponent Component { get; set; } =
        new(3062734, "api-call", "API Calls", "metered_component", isMetered: true, unitPrice: 0.01m);

    /// <summary>Every write the orchestration performed, in order.</summary>
    internal List<string> Calls { get; } = new();

    /// <summary>Thrown by <see cref="GetPeriodToDateUsageAsync"/> when set.</summary>
    internal BillingProviderException? PeriodToDateFailure { get; set; }

    /// <summary>Thrown by <see cref="GetMeteredComponentAsync"/> when set.</summary>
    internal BillingConfigurationException? ComponentFailure { get; set; }

    internal int PeriodToDateQuantity { get; set; }

    /// <summary>The subscription each write returns; defaults to the one that was acted on.</summary>
    internal Func<int, Subscription>? OnLifecycleAction { get; set; }

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("ListPlans");
        return Task.FromResult<IReadOnlyList<BillingPlan>>(Plans);
    }

    public Task<MeteredComponent> GetMeteredComponentAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("GetMeteredComponent");
        return ComponentFailure is not null ? throw ComponentFailure : Task.FromResult(Component);
    }

    /// <summary>
    /// Thrown by the customer lookup when set — the cheapest way to stand in for the provider
    /// being unreachable, since every flow starts there.
    /// </summary>
    internal Exception? LookupFailure { get; set; }

    public Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"FindCustomer:{reference}");

        if (LookupFailure is not null)
        {
            throw LookupFailure;
        }

        return Task.FromResult(Customer is not null &&
                               string.Equals(Customer.Reference, reference, StringComparison.OrdinalIgnoreCase)
            ? Customer
            : null);
    }

    public Task<BillingCustomer> CreateCustomerAsync(string reference,
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"CreateCustomer:{reference}:{firstName}:{lastName}");
        Customer = new BillingCustomer(90210, reference, email) { FirstName = firstName, LastName = lastName };
        return Task.FromResult(Customer);
    }

    public Task<IReadOnlyList<Subscription>> ListSubscriptionsForCustomerAsync(BillingCustomer customer,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"ListSubscriptions:{customer.Id}");
        return Task.FromResult<IReadOnlyList<Subscription>>(
            Subscriptions.Where(s => s.CustomerId == customer.Id).ToArray());
    }

    public Task<Subscription?> FindSubscriptionByIdAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"FindSubscription:{subscriptionId}");
        return Task.FromResult(Subscriptions.FirstOrDefault(s => s.Id == subscriptionId));
    }

    public Task<Subscription> CreateSubscriptionAsync(BillingCustomer customer,
        BillingPlan plan,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"CreateSubscription:{customer.Id}:{plan.Handle}");

        var created = new Subscription(1000 + Subscriptions.Count, customer.Reference, customer.Id, plan,
            SubscriptionState.Active, "active");

        Subscriptions.Add(created);
        return Task.FromResult(created);
    }

    public Task<UsageRecord> RecordUsageAsync(int subscriptionId,
        MeteredComponent component,
        int quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"RecordUsage:{subscriptionId}:{quantity}");
        return Task.FromResult(new UsageRecord(500, quantity, memo, DateTimeOffset.UtcNow));
    }

    public Task<int> GetPeriodToDateUsageAsync(Subscription subscription,
        MeteredComponent component,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"GetPeriodToDate:{subscription.Id}");
        return PeriodToDateFailure is not null
            ? throw PeriodToDateFailure
            : Task.FromResult(PeriodToDateQuantity);
    }

    public Task<PlanChangePreview> PreviewPlanChangeAsync(Subscription subscription,
        BillingPlan targetPlan,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"Preview:{subscription.Id}:{targetPlan.Handle}");

        return Task.FromResult(new PlanChangePreview(subscription.Id, subscription.Plan, targetPlan, timing,
            PreviewCharge, PreviewCredit, Math.Max(0m, PreviewCharge - PreviewCredit), null));
    }

    /// <summary>Lets a test move the quoted price between the preview and the commit.</summary>
    internal decimal PreviewCharge { get; set; } = 25.00m;

    internal decimal PreviewCredit { get; set; } = 10.00m;

    public Task<Subscription> ChangePlanAsync(Subscription subscription,
        BillingPlan targetPlan,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"ChangePlan:{subscription.Id}:{targetPlan.Handle}:{timing}");

        return Task.FromResult(Replace(subscription.Id, s =>
            new Subscription(s.Id, s.UserReference, s.CustomerId, targetPlan, s.State, s.ProviderState)));
    }

    public Task<Subscription> PauseSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"Pause:{subscriptionId}");
        return Task.FromResult(Transition(subscriptionId, SubscriptionState.Paused, "on_hold"));
    }

    public Task<Subscription> ResumeSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"Resume:{subscriptionId}");
        return Task.FromResult(Transition(subscriptionId, SubscriptionState.Active, "active"));
    }

    public Task<Subscription> CancelSubscriptionAsync(int subscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"Cancel:{subscriptionId}:{timing}");

        return Task.FromResult(timing == CancellationTiming.EndOfPeriod
            ? Replace(subscriptionId, s => new Subscription(s.Id, s.UserReference, s.CustomerId, s.Plan,
                s.State, s.ProviderState) { CancelAtEndOfPeriod = true })
            : Transition(subscriptionId, SubscriptionState.Canceled, "canceled"));
    }

    public Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"Reactivate:{subscriptionId}");
        return Task.FromResult(Transition(subscriptionId, SubscriptionState.Active, "active"));
    }

    private Subscription Transition(int subscriptionId, SubscriptionState state, string providerState) =>
        Replace(subscriptionId, s =>
            new Subscription(s.Id, s.UserReference, s.CustomerId, s.Plan, state, providerState));

    private Subscription Replace(int subscriptionId, Func<Subscription, Subscription> update)
    {
        var index = Subscriptions.FindIndex(s => s.Id == subscriptionId);
        if (index < 0)
        {
            throw new BillingProviderException($"No subscription {subscriptionId} in the fake provider.");
        }

        var updated = update(Subscriptions[index]);
        Subscriptions[index] = updated;
        return updated;
    }
}
