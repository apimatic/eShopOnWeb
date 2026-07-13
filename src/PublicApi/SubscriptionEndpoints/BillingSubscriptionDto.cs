using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class BillingSubscriptionDto
{
    public long Id { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public string State { get; set; } = string.Empty;
    public long BalanceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
}
