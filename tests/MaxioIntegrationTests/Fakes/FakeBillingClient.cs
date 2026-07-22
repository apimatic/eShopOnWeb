using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// A scripted stand-in for the provider seam, used to prove what the subscription service does — and,
/// just as importantly, what it refuses to do — without a provider in the picture.
/// </summary>
public sealed class FakeBillingClient : IBillingClient
{
    public List<string> Calls { get; } = new();

    public List<BillingPlan> Plans { get; } = new();
    public BillingCustomer? Customer { get; set; }
    public List<BillingSubscription> CustomerSubscriptions { get; } = new();
    public BillingSubscription? Subscription { get; set; }
    public BillingSubscription? UpdatedSubscription { get; set; }
    public BillingComponent UsageComponent { get; set; } =
        new(3057195, "api-call", "API Calls", "metered_component", true, 0.01m, "per_unit", "api call");
    public UsageRecord Usage { get; set; } = new(1, 1m, null, null, 3057195, "api-call");
    public decimal? PeriodToDateUsage { get; set; }
    public Exception? PeriodToDateFailure { get; set; }
    public PlanMigrationQuote Quote { get; set; } = new(0m, 0m, 0m, 0m);
    public string? LastPlanIdentifierSentToProvider { get; private set; }

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(ListPlansAsync));
        return Task.FromResult<IReadOnlyList<BillingPlan>>(Plans);
    }

    public Task<BillingPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(FindPlanAsync)}:{planHandle}");
        return Task.FromResult(Plans.FirstOrDefault(plan =>
            string.Equals(plan.Handle, planHandle, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(FindCustomerByReferenceAsync)}:{reference}");
        return Task.FromResult(Customer);
    }

    public Task<BillingCustomer> CreateCustomerAsync(string reference, string email, string firstName,
        string lastName, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(CreateCustomerAsync)}:{reference}:{email}:{firstName}:{lastName}");
        Customer = new BillingCustomer(88001, reference, email, firstName, lastName);
        return Task.FromResult(Customer);
    }

    public Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(CreateSubscriptionAsync)}:{customerId}:{planHandle}");
        LastPlanIdentifierSentToProvider = planHandle;
        return Task.FromResult(Subscription ?? throw new InvalidOperationException("No subscription scripted."));
    }

    public Task<BillingSubscription?> GetSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(GetSubscriptionAsync)}:{subscriptionId}");
        return Task.FromResult(Subscription);
    }

    public Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsForCustomerAsync(int customerId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(ListSubscriptionsForCustomerAsync)}:{customerId}");
        return Task.FromResult<IReadOnlyList<BillingSubscription>>(CustomerSubscriptions);
    }

    public Task<BillingComponent> GetUsageComponentAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(GetUsageComponentAsync));
        return Task.FromResult(UsageComponent);
    }

    public Task<UsageRecord> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(RecordUsageAsync)}:{subscriptionId}:{quantity}");
        Usage = Usage with { Quantity = quantity, Memo = memo };
        return Task.FromResult(Usage);
    }

    public Task<decimal?> GetPeriodToDateUsageAsync(int subscriptionId, int componentId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(GetPeriodToDateUsageAsync)}:{subscriptionId}:{componentId}");

        return PeriodToDateFailure is not null
            ? Task.FromException<decimal?>(PeriodToDateFailure)
            : Task.FromResult(PeriodToDateUsage);
    }

    public Task<PlanMigrationQuote> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(PreviewPlanChangeAsync)}:{subscriptionId}:{targetPlanHandle}");
        LastPlanIdentifierSentToProvider = targetPlanHandle;
        return Task.FromResult(Quote);
    }

    public Task<BillingSubscription> MigratePlanAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(MigratePlanAsync)}:{subscriptionId}:{targetPlanHandle}");
        LastPlanIdentifierSentToProvider = targetPlanHandle;
        return Task.FromResult(UpdatedSubscription ?? Subscription!);
    }

    public Task<BillingSubscription> SchedulePlanChangeAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(SchedulePlanChangeAsync)}:{subscriptionId}:{targetPlanHandle}");
        LastPlanIdentifierSentToProvider = targetPlanHandle;
        return Task.FromResult(UpdatedSubscription ?? Subscription!);
    }

    public Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(PauseSubscriptionAsync)}:{subscriptionId}");
        return Task.FromResult(UpdatedSubscription ?? Subscription!);
    }

    public Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(ResumeSubscriptionAsync)}:{subscriptionId}");
        return Task.FromResult(UpdatedSubscription ?? Subscription!);
    }

    public Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, string? reason,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(CancelSubscriptionAsync)}:{subscriptionId}:{reason}");
        return Task.FromResult(UpdatedSubscription ?? Subscription!);
    }

    public Task<BillingSubscription> ScheduleCancellationAsync(int subscriptionId, string? reason,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(ScheduleCancellationAsync)}:{subscriptionId}:{reason}");
        return Task.FromResult(UpdatedSubscription ?? Subscription!);
    }

    public Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(ReactivateSubscriptionAsync)}:{subscriptionId}");
        return Task.FromResult(UpdatedSubscription ?? Subscription!);
    }

    /// <summary>True when nothing at all was asked of the provider.</summary>
    public bool WasNeverCalled => Calls.Count == 0;

    public bool Called(string operation) => Calls.Any(call => call.StartsWith(operation, StringComparison.Ordinal));

    public static BillingSubscription SubscriptionInState(BillingSubscriptionState state,
        string planHandle = "eshop-pro", string reference = BillingClientFixture.UserReference) =>
        new(15236915, state, state.ToString().ToLowerInvariant(), 88001, reference, 7126957, planHandle,
            "Pro Plan", 299.00m, 0m, "USD", DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"), false, null, null);

    public static BillingPlan Plan(string handle, decimal price) =>
        new(handle == "eshop-pro" ? 7126957 : 7126958, handle, handle, price, 1, "month", false, false);

    public static readonly BillingProviderException ProviderDown =
        new BillingProviderUnavailableException("provider down", "GetPeriodToDateUsageAsync");
}
