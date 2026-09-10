using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscribable plan as returned by the API.</summary>
public class SubscriptionPlanModel
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string DisplayPrice { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }

    public static SubscriptionPlanModel From(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        DisplayPrice = FormatPrice(plan.Price, plan.Interval, plan.IntervalUnit)
    };

    private static string FormatPrice(decimal price, int? interval, string? unit)
    {
        var amount = price.ToString("0.00", CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(unit))
        {
            return amount;
        }
        var phrase = interval is > 1 ? $"{interval} {unit}s" : unit;
        return $"{amount} per {phrase}";
    }
}

/// <summary>A subscription belonging to the current user.</summary>
public class MySubscriptionModel
{
    public int Id { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }

    public static MySubscriptionModel From(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        State = subscription.State,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingDate = subscription.NextBillingDate
    };
}

/// <summary>Response body for <c>GET /api/subscription-plans</c>.</summary>
public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanModel> Plans { get; set; } = new();
}

/// <summary>Response body for <c>GET /api/my-subscriptions</c>.</summary>
public class ListMySubscriptionsResponse
{
    public List<MySubscriptionModel> Subscriptions { get; set; } = new();
}

/// <summary>Response body for <c>POST /api/subscriptions</c>.</summary>
public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }

    /// <summary>True when an existing live subscription was returned instead of creating a new one.</summary>
    public bool AlreadySubscribed { get; set; }
}

/// <summary>Error body returned when a subscription operation fails.</summary>
public class SubscriptionErrorResponse
{
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>Maps a <see cref="SubscriptionBillingException"/> to an appropriate HTTP result.</summary>
public static class SubscriptionErrorMapper
{
    public static ActionResult ToActionResult(SubscriptionBillingException ex)
    {
        int status;
        if (ex.IsCallerError)
        {
            // The caller can act on it — surface the provider status (default 400) and the message.
            status = ex.StatusCode ?? 400;
        }
        else
        {
            // Our credentials/quota, a transport failure, or a timeout — never the caller's fault. Do not
            // leak an upstream 401/403/429 as-is; a timeout maps to 504, everything else to 502.
            status = ex.StatusCode == 504 ? 504 : 502;
        }

        return new ObjectResult(new SubscriptionErrorResponse { StatusCode = status, Message = ex.Message })
        {
            StatusCode = status
        };
    }
}
