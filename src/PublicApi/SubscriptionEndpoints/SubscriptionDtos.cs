using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan as exposed to API callers.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

/// <summary>
/// A subscription as reflected in the billing system of record.
/// </summary>
public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
    public bool AlreadySubscribed { get; set; }
}

/// <summary>
/// Error body for billing endpoint failures. Carries caller-safe messages only.
/// </summary>
public class BillingErrorResponse : BaseResponse
{
    public BillingErrorResponse(Guid correlationId) : base(correlationId)
    {
    }

    public string Message { get; set; } = string.Empty;
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
}