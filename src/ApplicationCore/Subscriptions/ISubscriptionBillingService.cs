using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public record SubscriptionPlan(
    string Handle, string Name, string? Description, decimal Price,
    int IntervalCount, string IntervalUnit, bool TrialOffered, decimal SetupFee, bool RequiresPaymentMethod);

public record SubscriptionPlanList(IReadOnlyList<SubscriptionPlan> Plans, bool IsComplete);

public record SubscriptionView(
    string PlanHandle, string? PlanName, decimal? Price, string State,
    DateTimeOffset? NextBillingDate, int? SubscriptionId, string? Reference);

public enum SubscribeOutcome
{
    Created,
    Existing,
    InProgress,
    PlanNotAvailable,
    Refused,
    ProviderUnavailable
}

public record SubscribeResult(SubscribeOutcome Outcome, SubscriptionView? Subscription = null, string? Message = null);

public record BillingUser(string UserId, string? Email, string? UserName);

public interface ISubscriptionBillingService
{
    Task<SubscriptionPlanList> ListPlansAsync(CancellationToken ct);
    Task<SubscribeResult> SubscribeAsync(BillingUser user, string? planHandle, CancellationToken ct);
    Task<IReadOnlyList<SubscriptionView>> ListMySubscriptionsAsync(BillingUser user, CancellationToken ct);
}

/// <summary>Raised when the billing system cannot be reached or answers with something unusable.</summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message, int? statusCode = null, bool outcomeUnknown = false, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        OutcomeUnknown = outcomeUnknown;
    }

    public int? StatusCode { get; }
    /// <summary>True for a write whose connection failed after the provider may have acted.</summary>
    public bool OutcomeUnknown { get; }
    public bool IsCallerRefusal => StatusCode is >= 400 and < 500 && StatusCode is not (401 or 403 or 429 or 404);
}
