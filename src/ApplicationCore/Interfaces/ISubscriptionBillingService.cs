using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing. Failures surface as <see cref="Exceptions.BillingException"/>
/// (or <see cref="Exceptions.DuplicateException"/> while the same subscribe request is still in flight).
/// </summary>
public interface ISubscriptionBillingService
{
    Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(BillingCustomerProfile customer, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken);
}
