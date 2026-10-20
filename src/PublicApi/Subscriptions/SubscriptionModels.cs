using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public int? TrialInterval { get; set; }

    /// <summary>true when Maxio requires a payment method on file before this plan can be subscribed to.</summary>
    public bool? RequiresPaymentMethod { get; set; }
}

public class PlanListing
{
    public IReadOnlyList<SubscriptionPlan> Plans { get; set; } = Array.Empty<SubscriptionPlan>();

    /// <summary>false when the page cap cut the walk short, so the list may be missing plans.</summary>
    public bool IsComplete { get; set; } = true;
}

public class SubscriptionView
{
    public string PlanHandle { get; set; } = string.Empty;
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }

    /// <summary>Maxio subscription state (e.g. active, trialing) or "processing" / "unknown" while it is being settled.</summary>
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public int? SubscriptionId { get; set; }

    /// <summary>false when Maxio could not be reached and the values come from the last stored copy.</summary>
    public bool IsLive { get; set; } = true;
}

/// <summary>What the gateway reads off a Maxio subscription.</summary>
public record MaxioSubscriptionInfo(
    int Id,
    int? CustomerId,
    string? State,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    DateTimeOffset? NextAssessmentAt,
    DateTimeOffset? CurrentPeriodEndsAt);

public enum MaxioFailureKind
{
    /// <summary>Maxio answered 4xx: the request itself was refused. Nothing was created.</summary>
    Rejected,

    /// <summary>Our credentials/quota or the service is down, for a call that cannot have changed state.</summary>
    Unavailable,

    /// <summary>A write may or may not have landed (connection lost, timeout, 5xx, unreadable success body).</summary>
    OutcomeUnknown
}

public sealed class MaxioException : Exception
{
    public MaxioException(MaxioFailureKind kind, string message, int? statusCode = null, Exception? inner = null) : base(message, inner)
    {
        Kind = kind;
        StatusCode = statusCode;
    }

    public MaxioFailureKind Kind { get; }
    public int? StatusCode { get; }
}

/// <summary>A failure with a caller-safe message and the HTTP status to return.</summary>
public sealed class SubscriptionFlowException : Exception
{
    public SubscriptionFlowException(int statusCode, string code, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}

public record SubscribeCommand(string UserId, string? Email, string? UserName, string? PlanHandle);
