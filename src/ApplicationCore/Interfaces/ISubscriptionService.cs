using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Subscription capability backed by the external billing system of record.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the subscribable plans offered to shoppers.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync();

    /// <summary>
    /// Ensures a billing customer exists for the named shopper, then enrolls them in the
    /// requested plan. Idempotent: repeated calls never create duplicate billing
    /// customers or duplicate live subscriptions.
    /// </summary>
    Task<SubscriptionEnrollment> SubscribeAsync(string userName, string planHandle);

    /// <summary>Lists the subscriptions the named shopper owns in the billing system.</summary>
    Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsAsync(string userName);
}
