using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class UserSubscription
{
    public int Id { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string? State { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public long ProductPriceInCents { get; set; }
}
