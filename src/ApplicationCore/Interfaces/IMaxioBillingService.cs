using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing operations backed by Maxio Advanced Billing,
/// the billing system of record. The customer reference is the eShopOnWeb
/// identity user id, stored on the Maxio customer as its reference so
/// customer/subscription creation stays idempotent.
/// </summary>
public interface IMaxioBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionDetails> SubscribeAsync(string customerReference, string email, string displayName, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
