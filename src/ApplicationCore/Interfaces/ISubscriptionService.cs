using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<BillingPlanCatalog> GetPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Idempotently subscribes the shopper: ensures a billing customer exists and enrolls it in the plan.
    /// Repeating the call for the same plan returns the existing subscription instead of creating another.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken);

    Task<IReadOnlyList<BillingSubscription>> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken);
}

public sealed record SubscribeCommand(string UserName, string Email, string PlanHandle);

/// <param name="Created">False when an existing subscription was returned instead of creating a new one.</param>
public sealed record SubscribeResult(BillingSubscription Subscription, bool Created);
