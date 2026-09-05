using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

public class SubscriptionDto
{
    public long? Id { get; set; }
    public string? State { get; set; }
    public string? ProductName { get; set; }
    public string? ProductHandle { get; set; }
    public long? CurrentBillingAmountInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
