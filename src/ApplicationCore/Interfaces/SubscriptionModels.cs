using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public enum SubscriptionLifecycleAction
{
    Pause,
    Resume,
    Cancel,
    Reactivate
}

public record SubscriptionPlanDto(string Handle, string Name, long PriceInCents, int IntervalCount, string IntervalUnit);

public record SubscriptionDto(
    int SubscriptionId,
    string? ProductHandle,
    string? ProductName,
    string State,
    long? PriceInCents,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt,
    DateTimeOffset? DelayedCancelAt);

public record UsageResultDto(long UsageId, double QuantityRecorded, int? PeriodToDateUnits, bool PeriodToDateAvailable);

public record PlanChangePreviewDto(
    Guid PreviewToken,
    int SubscriptionId,
    string FromProductHandle,
    string ToProductHandle,
    bool ApplyAtRenewal,
    long? ProratedAdjustmentInCents,
    long? ChargeInCents,
    long? PaymentDueInCents,
    long? CreditAppliedInCents,
    long? NewPlanPriceInCents,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// The subscription-module use-case surface, mirroring IOrderService: orchestrates the billing
/// client, applies eShopOnWeb-side validation, and publishes MediatR notifications on state changes.
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default);

    Task<SubscriptionDto> SubscribeAsync(string userReference, string email, string firstName, string lastName, string productHandle, CancellationToken ct = default);

    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsForUserAsync(string userReference, CancellationToken ct = default);

    Task<UsageResultDto> RecordUsageAsync(int subscriptionId, string requestingUserReference, bool isAdmin, double quantity, string? memo, CancellationToken ct = default);

    Task<PlanChangePreviewDto> PreviewPlanChangeAsync(int subscriptionId, string requestingUserReference, bool isAdmin, string targetProductHandle, bool applyAtRenewal, CancellationToken ct = default);

    Task<SubscriptionDto> CommitPlanChangeAsync(int subscriptionId, string requestingUserReference, bool isAdmin, Guid previewToken, CancellationToken ct = default);

    Task<SubscriptionDto> ChangeLifecycleStateAsync(int subscriptionId, string requestingUserReference, bool isAdmin, SubscriptionLifecycleAction action, bool endOfPeriod, string? reason, CancellationToken ct = default);
}
