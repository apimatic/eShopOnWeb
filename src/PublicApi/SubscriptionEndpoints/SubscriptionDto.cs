namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public string? Reference { get; set; }
    public string? Currency { get; set; }
    public long BalanceInCents { get; set; }
    public long PriceInCents { get; set; }

    public decimal Price => PriceInCents / 100m;

    public decimal Balance => BalanceInCents / 100m;

    public string? PaymentCollectionMethod { get; set; }

    public System.DateTimeOffset? NextBillingDate { get; set; }
    public System.DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public System.DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public System.DateTimeOffset? NextAssessmentAt { get; set; }
    public System.DateTimeOffset? ActivatedAt { get; set; }
    public System.DateTimeOffset? CreatedAt { get; set; }
    public System.DateTimeOffset? CanceledAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
}
