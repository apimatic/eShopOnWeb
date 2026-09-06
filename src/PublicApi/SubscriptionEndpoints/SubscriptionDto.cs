using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public record SubscriptionDto
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public decimal BalanceInCents { get; init; }
    public decimal ProductPriceInCents { get; init; }
    public DateTime? CurrentPeriodEndsAt { get; init; }
    public DateTime? NextAssessmentAt { get; init; }
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public DateTime? CreatedAt { get; init; }
}
