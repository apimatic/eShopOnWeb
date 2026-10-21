using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record.
/// Runs alongside (not instead of) the one-time basket/order checkout.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>The plans offered in the configured catalog.</summary>
    Task<BillingResult<SubscriptionPlanCatalog>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Ensures a billing customer exists for the account and enrolls it in the plan.
    /// Safe to call twice: a repeat returns the existing subscription instead of creating another.</summary>
    Task<BillingResult<SubscribeOutcome>> SubscribeAsync(BillingAccount account, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>The account's subscriptions as the billing system currently holds them.</summary>
    Task<BillingResult<IReadOnlyList<SubscriptionSummary>>> ListSubscriptionsAsync(BillingAccount account, CancellationToken cancellationToken = default);
}

/// <summary>The shopper being billed. <see cref="UserId"/> is the stable identity key.</summary>
public sealed record BillingAccount(string UserId, string Email, string? FirstName = null, string? LastName = null);

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    long? InitialChargeInCents,
    long? TrialPriceInCents,
    int? TrialInterval,
    string? TrialIntervalUnit);

/// <param name="IsTruncated">True when the catalog had more plans than the read cap allowed;
/// <see cref="Plans"/> is then only the first part of the catalog.</param>
public sealed record SubscriptionPlanCatalog(string ProductFamilyHandle, IReadOnlyList<SubscriptionPlan> Plans, bool IsTruncated);

public sealed record SubscriptionSummary(
    int Id,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    string State,
    bool IsLive,
    long? PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? NextAssessmentAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CreatedAt);

/// <param name="AlreadyExisted">True when no new subscription was created because the account
/// already holds a live subscription to the requested plan.</param>
public sealed record SubscribeOutcome(SubscriptionSummary Subscription, bool AlreadyExisted);

public enum BillingResultStatus
{
    /// <summary>The operation completed.</summary>
    Success,
    /// <summary>The caller's input is invalid (e.g. an unknown plan).</summary>
    Invalid,
    /// <summary>The request conflicts with the account's current state.</summary>
    Conflict,
    /// <summary>Another request for the same account is still being processed.</summary>
    InProgress,
    /// <summary>The billing system refused the request; <see cref="BillingResult{T}.Errors"/> says why.</summary>
    Rejected,
    /// <summary>The write was sent but its outcome is not confirmed yet; it is settled on the next call.</summary>
    Pending,
    /// <summary>The billing system could not be reached or answered unusably; retrying later is safe.</summary>
    Unavailable
}

public sealed class BillingResult<T>
{
    private BillingResult(BillingResultStatus status, T? value, string? message, IReadOnlyList<string> errors)
    {
        Status = status;
        Value = value;
        Message = message;
        Errors = errors;
    }

    public BillingResultStatus Status { get; }
    public T? Value { get; }
    public string? Message { get; }
    public IReadOnlyList<string> Errors { get; }
    public bool IsSuccess => Status == BillingResultStatus.Success;

    public static BillingResult<T> Success(T value) => new(BillingResultStatus.Success, value, null, Array.Empty<string>());

    public static BillingResult<T> Failure(BillingResultStatus status, string message, IReadOnlyList<string>? errors = null)
    {
        if (status == BillingResultStatus.Success)
        {
            throw new ArgumentException("A failure cannot carry the Success status.", nameof(status));
        }
        return new(status, default, message, errors ?? Array.Empty<string>());
    }

    /// <summary>Carries a failure across to a result of another type.</summary>
    public BillingResult<TOther> As<TOther>() => BillingResult<TOther>.Failure(Status, Message ?? string.Empty, Errors);
}
