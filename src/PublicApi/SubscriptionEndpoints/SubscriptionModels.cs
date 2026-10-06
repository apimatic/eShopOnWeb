using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan available for purchase.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Recurring price in cents.</summary>
    public long PriceInCents { get; set; }
    /// <summary>Recurring price formatted as a decimal amount, e.g. "299.00".</summary>
    public string Price { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public int? TrialDays { get; set; }
    /// <summary>True when a payment method is required to subscribe (these plans do not).</summary>
    public bool RequiresPaymentMethod { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// A subscription held by the calling user.
/// </summary>
public class SubscriptionDto
{
    public int MaxioSubscriptionId { get; set; }
    public int MaxioCustomerId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ListSubscriptionPlansRequest : BaseRequest
{
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to, e.g. "eshop-pro".</summary>
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();
    /// <summary>True when the subscription already existed (idempotent replay).</summary>
    public bool WasExisting { get; set; }
}

public class ListMySubscriptionsRequest : BaseRequest
{
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}