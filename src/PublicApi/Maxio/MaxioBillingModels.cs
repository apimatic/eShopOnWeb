using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    /// <summary>true when Maxio would demand a payment method at signup; null when unknown.</summary>
    public bool? RequiresPaymentMethod { get; set; }
}

public class SubscriptionDto
{
    public int? Id { get; set; }
    public string? Reference { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}

public record PlanListResult(IReadOnlyList<SubscriptionPlanDto> Plans, bool IsComplete);

public record SubscribeResult(SubscriptionDto Subscription, bool AlreadySubscribed);

/// <summary>A billing failure already translated into a caller-safe status and message.</summary>
public class BillingException : Exception
{
    public int Status { get; }
    /// <summary>The provider definitively refused: nothing was created.</summary>
    public bool Refused { get; }
    /// <summary>The write may or may not have landed at the provider.</summary>
    public bool OutcomeUnknown { get; }

    public BillingException(int status, string message, bool refused = false, bool outcomeUnknown = false,
        Exception? inner = null) : base(message, inner)
    {
        Status = status;
        Refused = refused;
        OutcomeUnknown = outcomeUnknown;
    }
}
