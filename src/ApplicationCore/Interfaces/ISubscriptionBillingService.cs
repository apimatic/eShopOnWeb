using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by the external billing system of record.
/// Failures talking to the billing system surface as <see cref="Exceptions.BillingProviderException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<SubscriptionPlanList> GetPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, string? firstName, string? lastName,
        CancellationToken cancellationToken = default);

    Task<MySubscriptionsResult> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}
