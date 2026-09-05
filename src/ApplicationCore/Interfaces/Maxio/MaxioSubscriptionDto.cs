using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Maxio;

public class MaxioSubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
}
