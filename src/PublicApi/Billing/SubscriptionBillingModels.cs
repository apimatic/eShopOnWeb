using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>Recurring-subscription billing for shoppers. The billing provider is the system of record.</summary>
public interface ISubscriptionBillingService
{
    /// <summary>Plans offered in the configured product family. <see cref="PlanCatalog.IsComplete"/> is false when the listing was capped.</summary>
    Task<PlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Enrolls the shopper in a plan. Idempotent per shopper: repeated calls never create a second customer or subscription.</summary>
    Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>The shopper's subscriptions as held by the billing provider.</summary>
    Task<MySubscriptions> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}

public record SubscriptionPlan(
    int? Id,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit,
    bool RequiresPaymentMethod)
{
    public decimal Price => PriceInCents / 100m;
}

public record PlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool IsComplete);

public record BillingSubscription(
    int Id,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    string? State,
    long? PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CreatedAt)
{
    public decimal? Price => PriceInCents / 100m;
}

public enum SubscribeStatus
{
    /// <summary>A new subscription was created by this call.</summary>
    Created,
    /// <summary>The shopper already holds this plan; nothing new was created.</summary>
    AlreadySubscribed,
    /// <summary>Another request for this shopper is still being processed.</summary>
    InProgress,
    /// <summary>The provider could not be reached to confirm the outcome; it is settled on the next call.</summary>
    OutcomeUnknown
}

public record SubscribeResult(SubscribeStatus Status, BillingSubscription? Subscription, string? Message = null);

public record MySubscriptions(IReadOnlyList<BillingSubscription> Subscriptions, string? EnrollmentStatus);

public enum BillingFailureKind
{
    /// <summary>The caller's request cannot succeed as sent (unknown plan, provider validation error).</summary>
    InvalidRequest,
    /// <summary>The request conflicts with the shopper's current enrollment.</summary>
    Conflict,
    /// <summary>The billing provider is unreachable, timed out, misconfigured or failed.</summary>
    Unavailable
}

/// <summary>
/// The single failure type the billing layer raises. <see cref="Exception.Message"/> is caller-safe;
/// provider internals stay on <see cref="Exception.InnerException"/> for logging.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(BillingFailureKind kind, string message, int? providerStatus = null,
        IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatus = providerStatus;
        Errors = errors ?? Array.Empty<string>();
    }

    public BillingFailureKind Kind { get; }
    public int? ProviderStatus { get; }
    public IReadOnlyList<string> Errors { get; }
}
