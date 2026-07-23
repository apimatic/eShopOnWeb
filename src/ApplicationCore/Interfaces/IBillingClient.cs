using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single, provider-agnostic seam onto the recurring-billing provider. Nothing else in the
/// application talks to the provider. Implementations normalize provider representations onto the
/// subscription domain types (money in major units, states as <see cref="SubscriptionState"/>) and
/// surface failures as <see cref="Exceptions.BillingProviderException"/>.
/// </summary>
public interface IBillingClient
{
    /// <summary>Lists the recurring plans customers can subscribe to.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves a plan from its durable handle, or null when the handle does not resolve.</summary>
    Task<SubscriptionPlan?> GetPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Resolves a usage-billed component from its durable handle, or null when it does not resolve.</summary>
    Task<MeteredComponent?> GetComponentByHandleAsync(string componentHandle, CancellationToken cancellationToken = default);

    /// <summary>Finds the provider customer created for an eShopOnWeb user, or null when there is none yet.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string customerReference, CancellationToken cancellationToken = default);

    /// <summary>Creates the provider customer for an eShopOnWeb user, keyed on the user reference.</summary>
    Task<BillingCustomer> CreateCustomerAsync(string customerReference, string email, CancellationToken cancellationToken = default);

    /// <summary>Enrolls an existing customer in a plan.</summary>
    Task<CustomerSubscription> CreateSubscriptionAsync(string customerReference, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription belonging to a customer, in any state.</summary>
    Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Reads a single subscription, or null when no subscription has that id.</summary>
    Task<CustomerSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Records consumption of a metered component against a subscription.</summary>
    Task<UsageRecord> RecordUsageAsync(int subscriptionId, string componentHandle, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>Reads the units accrued against a component so far this period, or null when unavailable.</summary>
    Task<decimal?> GetUsageBalanceAsync(int subscriptionId, string componentHandle, CancellationToken cancellationToken = default);

    /// <summary>Computes what moving to a plan would cost, without committing anything.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default);

    /// <summary>Moves a subscription onto a plan at the requested time.</summary>
    Task<CustomerSubscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>Temporarily stops billing a subscription.</summary>
    Task<CustomerSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Resumes billing a paused subscription.</summary>
    Task<CustomerSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription, either now or at the end of the period it has paid for.</summary>
    Task<CustomerSubscription> CancelSubscriptionAsync(int subscriptionId, CancellationTiming timing, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Restarts a cancelled subscription.</summary>
    Task<CustomerSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
