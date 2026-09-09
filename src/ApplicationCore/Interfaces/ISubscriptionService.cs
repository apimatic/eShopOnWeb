using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Identifies the eShopOnWeb user whose billing account is being managed.
/// </summary>
public record BillingUser(string UserId, string UserName, string Email);

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing as the
/// billing system of record.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the plans available for subscription.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the user to the plan identified by <paramref name="productHandle"/>.
    /// Idempotent: repeated calls for the same user and plan return the existing
    /// subscription instead of creating a second one.
    /// </summary>
    Task<SubscriptionSummary> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription the user currently has with the billing system.
    /// </summary>
    Task<IReadOnlyList<SubscriptionSummary>> GetMySubscriptionsAsync(
        BillingUser user,
        CancellationToken cancellationToken = default);
}
