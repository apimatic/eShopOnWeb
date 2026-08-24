using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

// Mirrors components/schemas/Subscription-Response.yaml
public class MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

// Mirrors components/schemas/Subscription.yaml (fields relevant to this integration)
public class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public long BalanceInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public string? Currency { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

// Mirrors components/schemas/Create-Subscription-Request.yaml
public class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

// Mirrors components/schemas/Create-Subscription.yaml (subset used by this integration)
public class MaxioCreateSubscription
{
    public string? ProductHandle { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerReference { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}
