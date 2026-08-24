using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by the billing system of record (Maxio Advanced Billing).
/// All lookups are by handle/reference — never by provider numeric ids, which are not stable.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the non-archived subscription plans in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently subscribes a user to a plan: ensures the billing customer exists (keyed on
    /// <paramref name="userName"/> as the customer reference), then creates the subscription with a
    /// deterministic reference so a retried/double-submitted call returns the existing subscription.
    /// </summary>
    Task<CustomerSubscriptionDto> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the user's subscriptions; empty when the user has never subscribed.</summary>
    Task<IReadOnlyList<CustomerSubscriptionDto>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}
