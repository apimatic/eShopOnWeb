using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Use cases of the subscription capability, consumed by the API endpoints.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>The plans (and metered add-ons) a shopper can subscribe to.</summary>
    Task<BillingCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently enrolls a shopper on a plan: guarantees one Maxio customer per eShopOnWeb
    /// account and one live enrollment per account/plan pair.
    /// </summary>
    Task<SubscribeOutcome> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// The shopper's enrollments, read from the billing system. Returns an empty list when the
    /// account has no billing customer yet.
    /// </summary>
    Task<IReadOnlyList<UserSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
