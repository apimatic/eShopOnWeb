using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    int IntervalCount,
    string IntervalUnit);

public record SubscriptionInfo(
    string SubscriptionId,
    string State,
    string PlanHandle,
    string PlanName,
    decimal Price,
    int IntervalCount,
    string IntervalUnit,
    DateTimeOffset? NextBillingDate);

public record BillingCustomer(string Reference, string Email, string FirstName, string LastName);

/// <summary>
/// Port to the billing system of record (Maxio Advanced Billing). Throws <see cref="Exceptions.BillingException"/>.
/// </summary>
public interface IBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken ct = default);

    /// <summary>Returns the billing customer id for the reference, or null when none exists.</summary>
    Task<int?> FindCustomerIdAsync(string reference, CancellationToken ct = default);

    Task<int> CreateCustomerAsync(BillingCustomer customer, CancellationToken ct = default);

    Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(int customerId, CancellationToken ct = default);

    Task<SubscriptionInfo> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken ct = default);
}
