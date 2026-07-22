using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure;

/// <summary>
/// An in-memory stand-in for the provider seam, so the orchestration rules that must hold *before* a
/// provider call is made can be asserted directly — including the calls that must never happen.
/// </summary>
public sealed class FakeBillingClient : IBillingClient
{
    public List<string> Calls { get; } = new();

    public List<SubscriptionPlan> Plans { get; } = new();

    public List<CustomerSubscription> Subscriptions { get; } = new();

    public MeteredComponent Component { get; set; } =
        new(3062732, "api-call", "API Calls", "metered_component", isMetered: true, unitPrice: 0.01m);

    public int? PeriodToDateUnits { get; set; } = 12;

    /// <summary>When set, the period-to-date read fails with this, to exercise the degraded path.</summary>
    public Exception? PeriodToDateFailure { get; set; }

    public PlanChangePreview? NextPreview { get; set; }

    /// <summary>A second preview returned on the commit-time re-price, to simulate a moved basis.</summary>
    public PlanChangePreview? RepricedPreview { get; set; }

    public Func<int, CustomerSubscription>? OnChangePlan { get; set; }

    public Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(ListPlansAsync));
        return Task.FromResult<IReadOnlyCollection<SubscriptionPlan>>(Plans);
    }

    public Task<SubscriptionPlan> GetPlanAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(GetPlanAsync)}:{planHandle}");

        var plan = Plans.FirstOrDefault(p => p.Handle == planHandle)
            ?? throw new BillingConfigurationException(nameof(GetPlanAsync), $"plan handle '{planHandle}' does not resolve");

        return Task.FromResult(plan);
    }

    public Task<MeteredComponent> GetMeteredComponentAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(GetMeteredComponentAsync));
        return Task.FromResult(Component);
    }

    public Task<BillingCustomer> EnsureCustomerAsync(string reference, string firstName, string lastName, string email,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(EnsureCustomerAsync)}:{reference}:{firstName} {lastName}");
        return Task.FromResult(new BillingCustomer(501, reference) { FirstName = firstName, LastName = lastName, Email = email });
    }

    public Task<CustomerSubscription> CreateSubscriptionAsync(string customerReference, string planHandle,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(CreateSubscriptionAsync)}:{planHandle}");

        var created = new CustomerSubscription(2001, SubscriptionLifecycleState.Active)
        {
            PlanHandle = planHandle,
            PlanName = planHandle,
            PlanPrice = Plans.FirstOrDefault(p => p.Handle == planHandle)?.Price ?? 0m,
            CustomerReference = customerReference,
            NextAssessmentAt = DateTimeOffset.UtcNow.AddDays(30)
        };

        Subscriptions.Add(created);
        return Task.FromResult(created);
    }

    public Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(string customerReference,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(ListSubscriptionsAsync)}:{customerReference}");
        return Task.FromResult<IReadOnlyCollection<CustomerSubscription>>(Subscriptions);
    }

    public Task<CustomerSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(GetSubscriptionAsync)}:{subscriptionId}");
        return Task.FromResult(Subscriptions.FirstOrDefault(s => s.Id == subscriptionId));
    }

    public Task<UsageReceipt> RecordUsageAsync(int subscriptionId, int quantity, string? memo,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(RecordUsageAsync)}:{subscriptionId}:{quantity}:{memo}");
        return Task.FromResult(new UsageReceipt(90001, quantity) { Memo = memo, ComponentHandle = Component.Handle });
    }

    public Task<int?> GetPeriodToDateUsageAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(GetPeriodToDateUsageAsync)}:{subscriptionId}");

        if (PeriodToDateFailure is not null)
        {
            throw PeriodToDateFailure;
        }

        return Task.FromResult(PeriodToDateUnits);
    }

    public Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(PreviewPlanChangeAsync)}:{subscriptionId}:{targetPlanHandle}");

        // The first call answers with NextPreview; a configured RepricedPreview answers every call
        // after it, which is how a basis that moved between preview and commit is simulated.
        var previous = NextPreview;
        if (RepricedPreview is not null)
        {
            NextPreview = RepricedPreview;
        }

        return Task.FromResult(previous
            ?? new PlanChangePreview(subscriptionId, "eshop-pro", targetPlanHandle, timing));
    }

    public Task<CustomerSubscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(ChangePlanAsync)}:{subscriptionId}:{targetPlanHandle}");

        if (OnChangePlan is not null)
        {
            return Task.FromResult(OnChangePlan(subscriptionId));
        }

        return Task.FromResult(new CustomerSubscription(subscriptionId, SubscriptionLifecycleState.Active)
        {
            PlanHandle = targetPlanHandle
        });
    }

    public Task<CustomerSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
        => Transition(nameof(PauseSubscriptionAsync), subscriptionId, SubscriptionLifecycleState.Paused);

    public Task<CustomerSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
        => Transition(nameof(ResumeSubscriptionAsync), subscriptionId, SubscriptionLifecycleState.Active);

    public Task<CustomerSubscription> CancelSubscriptionAsync(int subscriptionId, CancellationTiming timing,
        string? reason, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(CancelSubscriptionAsync)}:{subscriptionId}:{timing}");

        return Task.FromResult(timing == CancellationTiming.EndOfPeriod
            ? new CustomerSubscription(subscriptionId, SubscriptionLifecycleState.Active)
            {
                CancelAtEndOfPeriod = true,
                DelayedCancelAt = DateTimeOffset.UtcNow.AddDays(10)
            }
            : new CustomerSubscription(subscriptionId, SubscriptionLifecycleState.Canceled));
    }

    public Task<CustomerSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
        => Transition(nameof(ReactivateSubscriptionAsync), subscriptionId, SubscriptionLifecycleState.Active);

    private Task<CustomerSubscription> Transition(string call, int subscriptionId, SubscriptionLifecycleState state)
    {
        Calls.Add($"{call}:{subscriptionId}");
        return Task.FromResult(new CustomerSubscription(subscriptionId, state));
    }
}

/// <summary>Captures the in-process notifications the service publishes.</summary>
public sealed class RecordingPublisher : IPublisher
{
    public List<INotification> Published { get; } = new();

    /// <summary>When set, publication throws — best-effort eventing must swallow it.</summary>
    public bool Throws { get; set; }

    public Task Publish(object notification, CancellationToken cancellationToken = default)
        => Publish((INotification)notification, cancellationToken);

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        if (Throws)
        {
            throw new InvalidOperationException("a handler blew up");
        }

        Published.Add(notification);
        return Task.CompletedTask;
    }
}
