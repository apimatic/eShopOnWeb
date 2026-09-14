using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Subscription billing operations against Maxio Advanced Billing, the billing
/// system of record for eShopOnWeb subscriptions.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the plans available in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a Maxio customer exists for the given user and enrolls them in the
    /// plan identified by <paramref name="productHandle"/>. Idempotent: repeated
    /// calls for the same user and plan never create duplicate customers or
    /// subscriptions; the existing subscription's details are returned instead.
    /// </summary>
    Task<SubscriptionDetailsDto> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the subscriptions the given user holds in the billing system.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetailsDto>> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken);
}
