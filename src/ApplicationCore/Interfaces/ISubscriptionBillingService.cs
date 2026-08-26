using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.DTOs;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing operations, backed by the billing system of record (Maxio).
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>List the subscription plans available in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribe a user to a plan. Idempotent: the user is matched to a billing customer by a
    /// stable external reference, and a repeated subscribe for the same plan returns the
    /// existing subscription instead of creating a duplicate.
    /// </summary>
    Task<CustomerSubscriptionDto> SubscribeAsync(string username, string email, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>List the user's subscriptions. Empty when the user has never subscribed.</summary>
    Task<IReadOnlyList<CustomerSubscriptionDto>> ListSubscriptionsAsync(string username, CancellationToken cancellationToken = default);
}
