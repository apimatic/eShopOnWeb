using System;

namespace Microsoft.eShopWeb.PublicApi.Services.Maxio.Models;

public class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public long? BalanceInCents { get; set; }
    public long? ProductPriceInCents { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioSubscriptionRequest
{
    public MaxioSubscriptionDraft? Subscription { get; set; }
}

public class MaxioSubscriptionDraft
{
    public string? ProductHandle { get; set; }
    public string? CustomerReference { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}
