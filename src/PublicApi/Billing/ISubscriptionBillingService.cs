using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken ct);

    /// <summary>
    /// Idempotently subscribes the user to a plan: ensures the Maxio customer exists
    /// (lookup-then-create on the user id) and reuses an existing non-terminal
    /// subscription for the same product instead of creating a duplicate.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(BillingCustomer customer, string productHandle, CancellationToken ct);

    Task<IReadOnlyList<BillingSubscription>> ListMySubscriptionsAsync(string userId, CancellationToken ct);
}
