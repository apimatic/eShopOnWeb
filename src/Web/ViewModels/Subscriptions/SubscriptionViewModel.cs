using System;

namespace Microsoft.eShopWeb.Web.ViewModels.Subscriptions;

public class SubscriptionViewModel
{
    public long Id { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal BalanceInDollars { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
}
