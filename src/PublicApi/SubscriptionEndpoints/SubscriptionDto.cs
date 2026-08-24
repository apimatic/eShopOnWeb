using System;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }

    public static SubscriptionDto FromDetails(SubscriptionDetails details) => new()
    {
        Id = details.Id,
        State = details.State,
        PlanHandle = details.PlanHandle,
        PlanName = details.PlanName,
        PriceInCents = details.PriceInCents,
        Currency = details.Currency,
        ActivatedAt = details.ActivatedAt,
        CurrentPeriodEndsAt = details.CurrentPeriodEndsAt,
        NextBillingAt = details.NextBillingAt
    };
}
