using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public string? Currency { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool? RequiresCreditCard { get; set; }

    public static SubscriptionPlanDto From(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        PriceInCents = plan.PriceInCents,
        Currency = plan.Currency,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresCreditCard = plan.RequiresCreditCard,
    };
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public decimal? Price { get; set; }
    public long? PriceInCents { get; set; }
    public string? Currency { get; set; }
    public string? State { get; set; }
    public bool HasAccess { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? PaymentCollectionMethod { get; set; }

    public static SubscriptionDto From(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        ProductFamilyHandle = subscription.ProductFamilyHandle,
        Price = subscription.Price,
        PriceInCents = subscription.PriceInCents,
        Currency = subscription.Currency,
        State = subscription.State,
        HasAccess = subscription.HasAccess,
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CreatedAt = subscription.CreatedAt,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
    };
}

public class PendingSignupDto
{
    public string SignupId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ActionLink { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PendingSignupDto From(PendingSignup signup) => new()
    {
        SignupId = signup.SignupId,
        PlanHandle = signup.PlanHandle,
        Status = signup.Status,
        ActionLink = signup.ActionLink,
        CreatedAt = signup.CreatedAt,
    };
}
