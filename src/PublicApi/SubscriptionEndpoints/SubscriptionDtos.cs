using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A recurring plan as exposed over the API. Money is in dollars.</summary>
public class PlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }

    public static PlanDto From(BillingPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };
}

/// <summary>A subscription as exposed over the API. Money is in dollars.</summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal PlanPrice { get; set; }

    /// <summary>eShopOnWeb's normalized state.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>The billing provider's own state string.</summary>
    public string ProviderState { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStart { get; set; }
    public DateTimeOffset? CurrentPeriodEnd { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CancellationScheduledAt { get; set; }
    public bool CancellationPending { get; set; }

    /// <summary>The lifecycle actions that are legal from the current state.</summary>
    public IReadOnlyList<string> AllowedActions { get; set; } = Array.Empty<string>();

    public static SubscriptionDto From(Subscription subscription)
    {
        var actions = new List<string>();
        foreach (var action in subscription.AllowedActions)
        {
            actions.Add(action.ToString());
        }

        return new SubscriptionDto
        {
            Id = subscription.Id,
            CustomerReference = subscription.CustomerReference,
            CustomerId = subscription.CustomerId,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PlanPrice = subscription.PlanPrice,
            State = subscription.State.ToString(),
            ProviderState = subscription.ProviderState,
            CurrentPeriodStart = subscription.CurrentPeriodStart,
            CurrentPeriodEnd = subscription.CurrentPeriodEnd,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CancellationScheduledAt = subscription.CancellationScheduledAt,
            CancellationPending = subscription.CancellationPending,
            AllowedActions = actions
        };
    }
}

/// <summary>Period-to-date metered usage. Money is in dollars.</summary>
public class UsageSummaryDto
{
    public string ComponentHandle { get; set; } = string.Empty;
    public long? RecordId { get; set; }
    public int? RecordedQuantity { get; set; }
    public string? Memo { get; set; }
    public int? PeriodToDateQuantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? PeriodToDateCharge { get; set; }

    /// <summary>True when the running total could not be read; any recorded usage still stands.</summary>
    public bool TotalUnavailable { get; set; }

    public static UsageSummaryDto From(UsageSummary summary) => new()
    {
        ComponentHandle = summary.ComponentHandle,
        RecordId = summary.Record?.Id,
        RecordedQuantity = summary.Record?.Quantity,
        Memo = summary.Record?.Memo,
        PeriodToDateQuantity = summary.PeriodToDateQuantity,
        UnitPrice = summary.UnitPrice,
        PeriodToDateCharge = summary.PeriodToDateCharge,
        TotalUnavailable = summary.TotalUnavailable
    };
}

/// <summary>A prorated plan-change quote. Money is in dollars.</summary>
public class PlanChangePreviewDto
{
    public int SubscriptionId { get; set; }
    public string CurrentPlanHandle { get; set; } = string.Empty;
    public string TargetPlanHandle { get; set; } = string.Empty;
    public string Timing { get; set; } = string.Empty;
    public decimal ProrationCharge { get; set; }
    public decimal ProrationCredit { get; set; }

    /// <summary>The value to echo back as <c>previewedNetAmount</c> when committing the change.</summary>
    public decimal NetAmount { get; set; }

    public decimal NewPlanPrice { get; set; }
    public DateTimeOffset? EffectiveAt { get; set; }

    public static PlanChangePreviewDto From(PlanChangePreview preview) => new()
    {
        SubscriptionId = preview.SubscriptionId,
        CurrentPlanHandle = preview.CurrentPlanHandle,
        TargetPlanHandle = preview.TargetPlanHandle,
        Timing = preview.Timing.ToString(),
        ProrationCharge = preview.ProrationCharge,
        ProrationCredit = preview.ProrationCredit,
        NetAmount = preview.NetAmount,
        NewPlanPrice = preview.NewPlanPrice,
        EffectiveAt = preview.EffectiveAt
    };
}
