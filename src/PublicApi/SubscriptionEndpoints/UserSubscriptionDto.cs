using System;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class UserSubscriptionDto
{
    public long MaxioSubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal PriceAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static UserSubscriptionDto FromMaxio(MaxioSubscription s) => new()
    {
        MaxioSubscriptionId = s.Id,
        PlanHandle = s.ProductHandle ?? string.Empty,
        PlanName = s.ProductName ?? string.Empty,
        PriceAmount = s.PriceInCents / 100m,
        Currency = "USD",
        State = s.State,
        NextBillingAt = s.NextAssessmentAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        CreatedAt = s.CreatedAt,
    };
}
