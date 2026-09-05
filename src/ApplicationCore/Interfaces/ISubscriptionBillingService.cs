using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing. Additive to the existing
/// one-time catalog/basket/order flow.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlansAsync(CancellationToken ct = default);

    /// <summary>
    /// Ensures a billing customer exists for <paramref name="customerReference"/> and enrolls it on
    /// <paramref name="planHandle"/>. Idempotent: a repeat call for the same reference + plan returns
    /// the existing customer/subscription rather than creating duplicates.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(string customerReference, string email, string planHandle, CancellationToken ct = default);

    /// <summary>
    /// Returns an empty list when no billing customer exists yet for <paramref name="customerReference"/>.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetCustomerSubscriptionsAsync(string customerReference, CancellationToken ct = default);
}

public class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequiresPaymentMethod { get; set; }
}

public class CustomerSubscription
{
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
}
