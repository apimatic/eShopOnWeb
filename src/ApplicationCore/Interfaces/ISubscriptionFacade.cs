using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application-level subscription use-cases (the "hero" flow). Orchestrates the billing
/// port and identity resolution so the HTTP endpoints stay thin.
/// </summary>
public interface ISubscriptionFacade
{
    /// <summary>The configured product family whose plans are offered.</summary>
    string ProductFamilyHandle { get; }

    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently subscribes the caller to a plan: ensures a billing customer exists for
    /// the user, then enrolls them, guaranteeing a double-submit cannot create a second
    /// live subscription to the same plan.
    /// </summary>
    Task<SubscribeOutcome> SubscribeAsync(ClaimsPrincipal caller, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingSubscription>> GetMySubscriptionsAsync(ClaimsPrincipal caller, CancellationToken cancellationToken = default);
}
