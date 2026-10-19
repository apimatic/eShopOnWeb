using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record.
/// Failures surface as <see cref="Exceptions.SubscriptionBillingException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans shoppers can subscribe to (archived plans excluded).</summary>
    Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for <paramref name="subscriber"/> and enrolls it in the plan
    /// identified by <paramref name="planHandle"/>. Safe to call twice: a repeat for the same plan
    /// replays the existing subscription instead of creating a second one.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions the billing system currently holds for the subscriber.</summary>
    Task<MySubscriptions> GetSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default);
}
