using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A recurring plan (Maxio product) a customer can subscribe to.
/// </summary>
public record BillingPlan(int Id, string Handle, string Name, long PriceInCents, int IntervalCount, string IntervalUnit);

/// <summary>
/// The metered, pay-as-you-go component (e.g. api-call) attached to the product family.
/// </summary>
public record BillingComponent(int Id, string Handle, string Name, string Kind, bool IsMeteredKind);

/// <summary>
/// The billing-provider customer record linked to an eShopOnWeb user.
/// </summary>
public record BillingCustomer(int Id, string Reference, string Email);

/// <summary>
/// A subscription as it exists in the billing provider.
/// </summary>
public record BillingSubscription(
    int Id,
    int CustomerId,
    string? CustomerReference,
    int? ProductId,
    string? ProductHandle,
    string? ProductName,
    string State,
    long? ProductPriceInCents,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt,
    DateTimeOffset? DelayedCancelAt);

/// <summary>
/// The result of recording a unit of metered usage.
/// </summary>
public record BillingUsage(long Id, double QuantityRecorded, string? Memo);

/// <summary>
/// A prorated preview of moving a subscription to a different plan effective immediately.
/// </summary>
public record BillingPlanChangePreview(long? ProratedAdjustmentInCents, long? ChargeInCents, long? PaymentDueInCents, long? CreditAppliedInCents);

/// <summary>
/// Provider-agnostic seam for every Maxio Advanced Billing capability the subscription feature needs.
/// The single implementation (Infrastructure/Services/MaxioBillingClient) is the only class in the
/// solution allowed to talk to the billing provider.
/// </summary>
public interface IBillingClient
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads back the configured metered component and confirms it resolves to a metered-kind
    /// component on the product family. Throws <see cref="Exceptions.BillingProviderException"/> when
    /// the handle does not resolve or resolves to a non-metered component.
    /// </summary>
    Task<BillingComponent> GetMeteredUsageComponentAsync(CancellationToken ct = default);

    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default);

    Task<BillingCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken ct = default);

    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default);

    Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct = default);

    Task<BillingSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default);

    Task<BillingPlan?> GetPlanByHandleAsync(string productHandle, CancellationToken ct = default);

    Task<BillingUsage> RecordUsageAsync(int subscriptionId, double quantity, string? memo, CancellationToken ct = default);

    /// <summary>Returns the period-to-date unit balance, or null when the read-back could not be completed.</summary>
    Task<int?> TryGetComponentPeriodToDateUsageAsync(int subscriptionId, CancellationToken ct = default);

    Task<BillingPlanChangePreview> PreviewPlanChangeNowAsync(int subscriptionId, string targetProductHandle, CancellationToken ct = default);

    Task<BillingSubscription> CommitPlanChangeNowAsync(int subscriptionId, string targetProductHandle, CancellationToken ct = default);

    Task<BillingSubscription> SchedulePlanChangeAtRenewalAsync(int subscriptionId, string targetProductHandle, CancellationToken ct = default);

    Task<BillingSubscription> PauseAsync(int subscriptionId, CancellationToken ct = default);

    Task<BillingSubscription> ResumeAsync(int subscriptionId, CancellationToken ct = default);

    Task<BillingSubscription> CancelNowAsync(int subscriptionId, string? reason, CancellationToken ct = default);

    Task<BillingSubscription> CancelAtEndOfPeriodAsync(int subscriptionId, string? reason, CancellationToken ct = default);

    Task<BillingSubscription> ReactivateAsync(int subscriptionId, CancellationToken ct = default);
}
