using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long SubscriptionId { get; set; }

    public string? Reference { get; set; }

    public string State { get; set; } = string.Empty;

    public SubscriptionPlanDto? Plan { get; set; }

    public long PriceInCents { get; set; }

    public decimal Price { get; set; }

    public string? Currency { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }
}
