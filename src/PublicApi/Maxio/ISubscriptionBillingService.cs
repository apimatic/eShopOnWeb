using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public record SubscribeResult(SubscriptionDto Subscription, bool Created);

/// <summary>Subscription billing, with Maxio Advanced Billing as the system of record.</summary>
public interface ISubscriptionBillingService
{
    /// <summary>The plans the configured product family offers.</summary>
    Task<PlanList> ListPlansAsync(CancellationToken ct);

    /// <summary>Subscribes the identified shopper to a plan, idempotently.</summary>
    Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken ct);

    /// <summary>The shopper's subscriptions, refreshed from Maxio.</summary>
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userName, CancellationToken ct);
}
