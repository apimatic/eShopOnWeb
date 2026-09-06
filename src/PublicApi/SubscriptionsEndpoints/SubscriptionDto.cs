using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public long BalanceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
}
