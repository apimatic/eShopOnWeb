using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken ct = default);

    /// <summary>Idempotent: ensures the billing customer exists and never creates a second live subscription to the same plan.</summary>
    Task<SubscribeResult> SubscribeAsync(string userIdentity, string planHandle, CancellationToken ct = default);

    Task<IReadOnlyList<UserSubscription>> ListSubscriptionsAsync(string userIdentity, CancellationToken ct = default);
}
