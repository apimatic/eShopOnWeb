using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing. Failures surface as <see cref="Exceptions.BillingException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsAsync(string buyerId, CancellationToken cancellationToken = default);
}
