using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// An in-memory stand-in for the provider seam, used to test the orchestration above it.
/// </summary>
/// <remarks>
/// It counts calls so a test can prove that a rejection happened <em>before</em> the provider was
/// touched — which is what several of the plan's failure scenarios actually require.
/// </remarks>
internal sealed class FakeBillingClient : IBillingClient
{
    public List<SubscriptionPlan> Plans { get; } = new();

    public Dictionary<string, BillingCustomer> CustomersByReference { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<int, Subscription> Subscriptions { get; } = new();

    public Dictionary<int, List<Subscription>> SubscriptionsByCustomer { get; } = new();

    public MeteredComponent Component { get; set; } = new()
    {
        Handle = "api-call",
        ProviderId = 3062731,
        Name = "API Calls",
        Kind = "metered_component",
        IsMetered = true,
        PricingScheme = "per_unit",
        UnitPriceInCents = 1
    };

    public PlanChangePreview? NextPreview { get; set; }

    public decimal? PeriodToDateUnits { get; set; } = 0m;

    /// <summary>When set, reading the period-to-date total fails, exercising the degraded read-back path.</summary>
    public Exception? PeriodToDateFailure { get; set; }

    /// <summary>Counts every provider call, keyed by operation name.</summary>
    public Dictionary<string, int> Calls { get; } = new(StringComparer.Ordinal);

    public List<UsageRecord> RecordedUsage { get; } = new();

    public int CallCount => Calls.Values.Sum();

    public int CountOf(string operation) => Calls.TryGetValue(operation, out var count) ? count : 0;

    private void Record(string operation) =>
        Calls[operation] = Calls.TryGetValue(operation, out var count) ? count + 1 : 1;

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        Record(nameof(ListPlansAsync));
        return Task.FromResult<IReadOnlyList<SubscriptionPlan>>(Plans.ToList());
    }

    public Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        Record(nameof(FindPlanAsync));
        return Task.FromResult(Plans.FirstOrDefault(
            plan => string.Equals(plan.Handle, planHandle, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<MeteredComponent> GetMeteredComponentAsync(CancellationToken cancellationToken = default)
    {
        Record(nameof(GetMeteredComponentAsync));
        return Task.FromResult(Component);
    }

    public Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken = default)
    {
        Record(nameof(FindCustomerAsync));
        return Task.FromResult(CustomersByReference.TryGetValue(reference, out var customer) ? customer : null);
    }

    public Task<BillingCustomer> EnsureCustomerAsync(
        BillingCustomerRegistration registration,
        CancellationToken cancellationToken = default)
    {
        Record(nameof(EnsureCustomerAsync));

        if (!CustomersByReference.TryGetValue(registration.Reference, out var customer))
        {
            customer = new BillingCustomer
            {
                Id = CustomersByReference.Count + 1,
                Reference = registration.Reference,
                Email = registration.Email,
                FirstName = registration.FirstName,
                LastName = registration.LastName
            };

            CustomersByReference[registration.Reference] = customer;
        }

        return Task.FromResult(customer);
    }

    public Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        Record(nameof(ListSubscriptionsAsync));

        var subscriptions = SubscriptionsByCustomer.TryGetValue(customerId, out var list)
            ? list.ToList()
            : new List<Subscription>();

        return Task.FromResult<IReadOnlyList<Subscription>>(subscriptions);
    }

    public Task<Subscription?> GetSubscriptionAsync(
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        Record(nameof(GetSubscriptionAsync));
        return Task.FromResult(Subscriptions.TryGetValue(subscriptionId, out var subscription)
            ? subscription
            : null);
    }

    public Task<Subscription> CreateSubscriptionAsync(
        int customerId,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        Record(nameof(CreateSubscriptionAsync));

        var plan = Plans.First(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        var reference = CustomersByReference.Values.FirstOrDefault(c => c.Id == customerId)?.Reference;

        var subscription = new Subscription
        {
            Id = 9000 + Subscriptions.Count + 1,
            CustomerId = customerId,
            CustomerReference = reference,
            State = SubscriptionState.Active,
            ProviderState = "active",
            PlanHandle = plan.Handle,
            PlanName = plan.Name,
            PlanPriceInCents = plan.PriceInCents,
            CurrentPeriodStartedAt = DateTimeOffset.UtcNow,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
            NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1)
        };

        Add(subscription);
        return Task.FromResult(subscription);
    }

    public Task<UsageRecord> RecordUsageAsync(
        int subscriptionId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        Record(nameof(RecordUsageAsync));

        var usage = new UsageRecord
        {
            Id = RecordedUsage.Count + 1,
            SubscriptionId = subscriptionId,
            ComponentHandle = Component.Handle,
            Quantity = quantity,
            Memo = memo,
            RecordedAt = DateTimeOffset.UtcNow
        };

        RecordedUsage.Add(usage);
        PeriodToDateUnits = (PeriodToDateUnits ?? 0m) + quantity;

        return Task.FromResult(usage);
    }

    public Task<decimal?> GetPeriodToDateUsageAsync(
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        Record(nameof(GetPeriodToDateUsageAsync));

        if (PeriodToDateFailure is not null)
        {
            throw PeriodToDateFailure;
        }

        return Task.FromResult(PeriodToDateUnits);
    }

    public Task<PlanChangePreview> PreviewPlanChangeAsync(
        int subscriptionId,
        string targetPlanHandle,
        CancellationToken cancellationToken = default)
    {
        Record(nameof(PreviewPlanChangeAsync));

        var preview = NextPreview ?? new PlanChangePreview
        {
            SubscriptionId = subscriptionId,
            TargetPlanHandle = targetPlanHandle,
            ChargeInCents = 27_000L,
            CreditAppliedInCents = 0L,
            PaymentDueInCents = 27_000L,
            ProratedAdjustmentInCents = 27_000L,
            PreviewedAt = DateTimeOffset.UtcNow
        };

        return Task.FromResult(preview with { SubscriptionId = subscriptionId, TargetPlanHandle = targetPlanHandle });
    }

    public Task<Subscription> ChangePlanImmediatelyAsync(
        int subscriptionId,
        string targetPlanHandle,
        CancellationToken cancellationToken = default)
    {
        Record(nameof(ChangePlanImmediatelyAsync));
        return Task.FromResult(MoveToPlan(subscriptionId, targetPlanHandle, scheduled: false));
    }

    public Task<Subscription> SchedulePlanChangeAsync(
        int subscriptionId,
        string targetPlanHandle,
        CancellationToken cancellationToken = default)
    {
        Record(nameof(SchedulePlanChangeAsync));
        return Task.FromResult(MoveToPlan(subscriptionId, targetPlanHandle, scheduled: true));
    }

    public Task<Subscription> PauseSubscriptionAsync(
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        Record(nameof(PauseSubscriptionAsync));
        return Task.FromResult(Transition(subscriptionId, subscription => subscription with
        {
            State = SubscriptionState.Paused,
            ProviderState = "on_hold",
            PausedAt = DateTimeOffset.UtcNow
        }));
    }

    public Task<Subscription> ResumeSubscriptionAsync(
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        Record(nameof(ResumeSubscriptionAsync));
        return Task.FromResult(Transition(subscriptionId, subscription => subscription with
        {
            State = SubscriptionState.Active,
            ProviderState = "active",
            PausedAt = null
        }));
    }

    public Task<Subscription> CancelSubscriptionAsync(
        int subscriptionId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        Record(nameof(CancelSubscriptionAsync));
        return Task.FromResult(Transition(subscriptionId, subscription => subscription with
        {
            State = SubscriptionState.Canceled,
            ProviderState = "canceled",
            CanceledAt = DateTimeOffset.UtcNow
        }));
    }

    public Task<Subscription> CancelSubscriptionAtPeriodEndAsync(
        int subscriptionId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        Record(nameof(CancelSubscriptionAtPeriodEndAsync));
        return Task.FromResult(Transition(subscriptionId, subscription => subscription with
        {
            CancelAtEndOfPeriod = true,
            ScheduledCancellationAt = subscription.CurrentPeriodEndsAt
        }));
    }

    public Task<Subscription> ReactivateSubscriptionAsync(
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        Record(nameof(ReactivateSubscriptionAsync));
        return Task.FromResult(Transition(subscriptionId, subscription => subscription with
        {
            State = SubscriptionState.Active,
            ProviderState = "active",
            CanceledAt = null,
            CancelAtEndOfPeriod = false
        }));
    }

    public void Add(Subscription subscription)
    {
        Subscriptions[subscription.Id] = subscription;

        if (subscription.CustomerId is { } customerId)
        {
            if (!SubscriptionsByCustomer.TryGetValue(customerId, out var list))
            {
                list = new List<Subscription>();
                SubscriptionsByCustomer[customerId] = list;
            }

            list.RemoveAll(existing => existing.Id == subscription.Id);
            list.Add(subscription);
        }
    }

    private Subscription Transition(int subscriptionId, Func<Subscription, Subscription> change)
    {
        if (!Subscriptions.TryGetValue(subscriptionId, out var subscription))
        {
            throw new SubscriptionNotFoundException(subscriptionId);
        }

        var updated = change(subscription);
        Add(updated);
        return updated;
    }

    private Subscription MoveToPlan(int subscriptionId, string targetPlanHandle, bool scheduled)
    {
        var plan = Plans.First(p => string.Equals(p.Handle, targetPlanHandle, StringComparison.OrdinalIgnoreCase));

        return Transition(subscriptionId, subscription => scheduled
            ? subscription with { ScheduledPlanHandle = plan.Handle }
            : subscription with
            {
                PlanHandle = plan.Handle,
                PlanName = plan.Name,
                PlanPriceInCents = plan.PriceInCents
            });
    }
}
