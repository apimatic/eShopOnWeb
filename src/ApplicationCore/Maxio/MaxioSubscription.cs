using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public MaxioPlan? Plan { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
