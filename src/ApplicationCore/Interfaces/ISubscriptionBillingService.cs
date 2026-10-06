using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing against the billing system of record (Maxio Advanced Billing).
/// All operations are idempotent with respect to the caller's identity: repeated calls with the
/// same user and plan never create duplicate customers or subscriptions.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscriptionSummary> SubscribeAsync(string userEmail, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionSummary>> ListForUserAsync(string userEmail, CancellationToken cancellationToken);
}