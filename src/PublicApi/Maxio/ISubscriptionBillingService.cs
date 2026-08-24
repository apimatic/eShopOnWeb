using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The integration boundary over Maxio Advanced Billing. All SDK failures are translated
/// to <see cref="BillingException"/>; no SDK type crosses this boundary.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently subscribes the user to a plan: finds-or-creates the Maxio customer keyed
    /// on <paramref name="userReference"/>, returns the existing live subscription if one
    /// already exists for the plan, otherwise creates it.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string userReference, string email, string productHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);
}

public record SubscribeResult(SubscriptionDto Subscription, bool AlreadyExisted);
