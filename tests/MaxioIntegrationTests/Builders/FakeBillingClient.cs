using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// An in-memory stand-in for the provider seam, so the orchestration above it can be tested for
/// the guarantees it makes — ownership, idempotency, validation ordering — without HTTP.
/// </summary>
public sealed class FakeBillingClient : IBillingClient
{
    private readonly Dictionary<string, BillingCustomer> _customersByReference = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, Subscription> _subscriptions = new();

    private int _nextCustomerId = 500;
    private int _nextSubscriptionId = 900;
    private long _nextUsageId = 1;

    public List<BillingPlan> Plans { get; } = new()
    {
        new BillingPlan(2, "basic-plan", "Basic Plan", null, 2900, 1, "month", false, null),
        new BillingPlan(1, "eshop-pro", "Pro Plan", null, 29900, 1, "month", false, null)
    };

    public MeteredComponent? Component { get; set; } =
        new(3057195, "api-call", "API Calls", "api call", 1, IsMetered: true, IsArchived: false);

    /// <summary>Set to make <see cref="GetConfiguredMeteredComponentAsync"/> fail the seed check.</summary>
    public BillingConfigurationException? ComponentConfigurationFailure { get; set; }

    /// <summary>Set to make the period-to-date read-back fail.</summary>
    public BillingProviderException? PeriodToDateFailure { get; set; }

    public List<string> Calls { get; } = new();

    public int CreatedSubscriptionCount { get; private set; }

    public int CreatedCustomerCount { get; private set; }

    public Subscription SeedSubscription(
        string customerReference,
        SubscriptionState state = SubscriptionState.Active,
        string planHandle = "eshop-pro")
    {
        var customer = SeedCustomer(customerReference);
        var plan = Plans.Single(p => p.Handle == planHandle);

        var subscription = new Subscription(
            Id: ++_nextSubscriptionId,
            State: state,
            CustomerId: customer.Id,
            CustomerReference: customerReference,
            PlanId: plan.Id,
            PlanHandle: plan.Handle,
            PlanName: plan.Name,
            PlanPriceInCents: plan.PriceInCents,
            CurrentPeriodStartedAt: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            CurrentPeriodEndsAt: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            NextAssessmentAt: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            CancelAtEndOfPeriod: false,
            CanceledAt: null,
            NextPlanHandle: null);

        _subscriptions[subscription.Id] = subscription;

        return subscription;
    }

    public BillingCustomer SeedCustomer(string reference)
    {
        if (_customersByReference.TryGetValue(reference, out var existing))
        {
            return existing;
        }

        var customer = new BillingCustomer(++_nextCustomerId, reference, reference, "first", "last");
        _customersByReference[reference] = customer;

        return customer;
    }

    public Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(ListPlansAsync));
        return Task.FromResult<IReadOnlyCollection<BillingPlan>>(Plans.ToArray());
    }

    public Task<BillingPlan?> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(FindPlanByHandleAsync)}:{planHandle}");
        return Task.FromResult(Plans.FirstOrDefault(p => p.Handle == planHandle));
    }

    public Task<MeteredComponent?> FindMeteredComponentAsync(string componentHandle, CancellationToken cancellationToken = default) =>
        Task.FromResult(Component);

    public Task<MeteredComponent> GetConfiguredMeteredComponentAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(GetConfiguredMeteredComponentAsync));

        if (ComponentConfigurationFailure is not null)
        {
            throw ComponentConfigurationFailure;
        }

        return Task.FromResult(Component!);
    }

    public Task<BillingCustomer?> FindCustomerByReferenceAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(FindCustomerByReferenceAsync)}:{customerReference}");
        _customersByReference.TryGetValue(customerReference, out var customer);

        return Task.FromResult(customer);
    }

    public Task<BillingCustomer> EnsureCustomerAsync(BillingCustomerRegistration registration, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(EnsureCustomerAsync)}:{registration.Reference}");

        if (!_customersByReference.ContainsKey(registration.Reference))
        {
            CreatedCustomerCount++;
        }

        return Task.FromResult(SeedCustomer(registration.Reference));
    }

    public Task<Subscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(CreateSubscriptionAsync)}:{planHandle}");
        CreatedSubscriptionCount++;

        var reference = _customersByReference.Values.Single(c => c.Id == customerId).Reference;

        return Task.FromResult(SeedSubscription(reference, SubscriptionState.Active, planHandle));
    }

    public Task<IReadOnlyCollection<Subscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(ListSubscriptionsForCustomerAsync)}:{customerId}");

        return Task.FromResult<IReadOnlyCollection<Subscription>>(
            _subscriptions.Values.Where(s => s.CustomerId == customerId).ToArray());
    }

    public Task<Subscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        _subscriptions.TryGetValue(subscriptionId, out var subscription);
        return Task.FromResult(subscription);
    }

    public Task<UsageRecord> RecordUsageAsync(int subscriptionId, int componentId, decimal quantity, string? memo, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(RecordUsageAsync)}:{subscriptionId}:{quantity}");

        return Task.FromResult(new UsageRecord(_nextUsageId++, subscriptionId, componentId, quantity, memo, DateTimeOffset.UnixEpoch));
    }

    public Task<decimal> GetPeriodToDateUsageAsync(int subscriptionId, int componentId, DateTimeOffset? periodStart, DateTimeOffset? periodEnd, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(GetPeriodToDateUsageAsync));

        if (PeriodToDateFailure is not null)
        {
            throw PeriodToDateFailure;
        }

        return Task.FromResult(42m);
    }

    /// <summary>The amount every preview reports, so a test can move it to simulate staleness.</summary>
    public long PreviewPaymentDueInCents { get; set; } = 16400;

    public Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(PreviewPlanChangeAsync)}:{targetPlanHandle}");

        var current = _subscriptions[subscriptionId];

        return Task.FromResult(new PlanChangePreview(
            subscriptionId, current.PlanHandle, targetPlanHandle, timing, 0, 29900, 0, PreviewPaymentDueInCents));
    }

    public Task<Subscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(ChangePlanAsync)}:{targetPlanHandle}");

        var plan = Plans.Single(p => p.Handle == targetPlanHandle);
        var updated = _subscriptions[subscriptionId] with
        {
            PlanHandle = plan.Handle,
            PlanName = plan.Name,
            PlanPriceInCents = plan.PriceInCents
        };

        _subscriptions[subscriptionId] = updated;

        return Task.FromResult(updated);
    }

    public Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
        Transition(subscriptionId, SubscriptionState.Paused, nameof(PauseSubscriptionAsync));

    public Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
        Transition(subscriptionId, SubscriptionState.Active, nameof(ResumeSubscriptionAsync));

    public Task<Subscription> CancelSubscriptionAsync(int subscriptionId, CancellationTiming timing, string? reason, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(CancelSubscriptionAsync)}:{timing}:{reason}");

        return timing == CancellationTiming.EndOfPeriod
            ? Task.FromResult(Store(_subscriptions[subscriptionId] with { CancelAtEndOfPeriod = true }))
            : Task.FromResult(Store(_subscriptions[subscriptionId] with { State = SubscriptionState.Canceled }));
    }

    public Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
        Transition(subscriptionId, SubscriptionState.Active, nameof(ReactivateSubscriptionAsync));

    private Task<Subscription> Transition(int subscriptionId, SubscriptionState state, string call)
    {
        Calls.Add(call);
        return Task.FromResult(Store(_subscriptions[subscriptionId] with { State = state }));
    }

    private Subscription Store(Subscription subscription)
    {
        _subscriptions[subscription.Id] = subscription;
        return subscription;
    }
}
