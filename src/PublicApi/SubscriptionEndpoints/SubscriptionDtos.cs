using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan as offered to shoppers.
/// </summary>
public sealed class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public string Interval { get; set; } = string.Empty;
    public bool RequestCreditCard { get; set; }
}

/// <summary>
/// A Maxio subscription as confirmed back to the user.
/// </summary>
public sealed class SubscriptionDto
{
    public int Id { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public string? Currency { get; set; }
}

internal static class SubscriptionDtoMapper
{
    public static SubscriptionPlanDto ToDto(this Maxio.MaxioPlanInfo plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        RequestCreditCard = plan.RequestCreditCard
    };

    public static SubscriptionDto ToDto(this Maxio.MaxioSubscriptionInfo subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        State = subscription.State,
        Price = subscription.Price,
        PriceInCents = subscription.PriceInCents,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        NextBillingAt = subscription.NextBillingAt,
        ActivatedAt = subscription.ActivatedAt,
        Currency = subscription.Currency
    };
}
