using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Fronts Maxio Advanced Billing for the eShopOnWeb recurring-subscription capability.
/// Maxio is the billing system of record: plans and subscription state are always read live.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists the subscribable plans in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken ct = default);

    /// <summary>
    /// Lists the shopper's subscriptions, ensuring a Maxio customer exists for them first.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscriptionDto>> GetSubscriptionsForCustomerAsync(MaxioCustomerIdentity customer, CancellationToken ct = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the shopper and enrolls them in <paramref name="planHandle"/>.
    /// Idempotent: a shopper who already has a non-terminal subscription to the same plan gets that
    /// existing subscription back rather than a duplicate.
    /// </summary>
    Task<CustomerSubscriptionDto> SubscribeAsync(MaxioCustomerIdentity customer, string planHandle, CancellationToken ct = default);
}
