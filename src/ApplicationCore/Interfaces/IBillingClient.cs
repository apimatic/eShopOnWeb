using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider-agnostic seam onto the recurring-billing provider. Exactly one Infrastructure class
/// implements this, and nothing else in the application talks to the provider. Implementations
/// surface every provider failure as <see cref="Exceptions.BillingProviderException"/>.
/// </summary>
public interface IBillingClient
{
    /// <summary>Lists the recurring plans available in the configured product family.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads one plan by its durable handle, or null when no such plan exists.</summary>
    Task<SubscriptionPlan?> GetPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the provider customer for <paramref name="userReference"/>, creating it if absent.
    /// Idempotent on the reference, so repeated subscribe attempts never duplicate the customer.
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription belonging to a provider customer, newest state included.</summary>
    Task<IReadOnlyCollection<Subscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Reads one subscription, or null when the provider does not know that id.</summary>
    Task<Subscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Enrolls an existing provider customer in a plan.</summary>
    Task<Subscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the pay-as-you-go component the client is configured to meter, or null when the
    /// configured handle does not resolve. The caller checks the kind before recording usage.
    /// </summary>
    Task<MeteredComponent?> GetMeteredComponentAsync(CancellationToken cancellationToken = default);

    /// <summary>Records consumption of a metered component against a subscription.</summary>
    Task<UsageRecord> RecordUsageAsync(int subscriptionId, int componentId, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>Reads the accrued unit balance of a metered component on a subscription for the current period.</summary>
    Task<decimal> GetUsageTotalAsync(int subscriptionId, int componentId, CancellationToken cancellationToken = default);

    /// <summary>Computes what moving to <paramref name="targetPlanHandle"/> would cost, without committing.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(Subscription subscription, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>Commits a plan change with the chosen timing.</summary>
    Task<Subscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>Temporarily stops billing; the subscription can be resumed.</summary>
    Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Restarts billing on a paused subscription.</summary>
    Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription, either straight away or at the end of the current period.</summary>
    Task<Subscription> CancelSubscriptionAsync(int subscriptionId, CancellationTiming timing, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Restarts a cancelled subscription.</summary>
    Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
