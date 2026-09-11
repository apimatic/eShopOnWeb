using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response from creating a subscription
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse() : base() { }
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public SubscriptionDto? Subscription { get; set; }
}

/// <summary>
/// Response containing user's subscriptions
/// </summary>
public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse() : base() { }

    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

/// <summary>
/// DTO for a subscription
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public CustomerDto? Customer { get; set; }

    public string FormattedPrice => $"${ProductPriceInCents / 100.0:F2}";

    public string StateLabel => State.ToLowerInvariant() switch
    {
        "active" => "Active",
        "trialing" => "Trialing",
        "past_due" => "Past Due",
        "canceled" => "Canceled",
        "expired" => "Expired",
        "pending" => "Pending",
        "suspended" => "Suspended",
        "unpaid" => "Unpaid",
        _ => State
    };
}

/// <summary>
/// DTO for customer info
/// </summary>
public class CustomerDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

/// <summary>
/// DTO for a subscription plan
/// </summary>
public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public string? ProductFamilyName { get; set; }
    public string? ProductFamilyHandle { get; set; }

    public string FormattedPrice => $"${PriceInCents / 100.0:F2}";

    public string BillingInterval => IntervalUnit.ToLowerInvariant() switch
    {
        "month" => Interval == 1 ? "Monthly" : $"Every {Interval} months",
        "day" => Interval == 1 ? "Daily" : $"Every {Interval} days",
        _ => $"{Interval} {IntervalUnit}"
    };
}
