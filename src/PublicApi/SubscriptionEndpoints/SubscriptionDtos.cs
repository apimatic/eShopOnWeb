using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscribable plan as exposed over the API.</summary>
public class PlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>The recurring price in cents — the provider's canonical money unit.</summary>
    public long PriceInCents { get; set; }

    /// <summary>The recurring price in the site's currency unit, derived from the cents value.</summary>
    public decimal Price { get; set; }

    public string BillingPeriod { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }

    public static PlanDto FromPlan(BillingPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        BillingPeriod = plan.BillingPeriod,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };
}

/// <summary>A customer subscription as exposed over the API.</summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PlanPriceInCents { get; set; }
    public decimal PlanPrice { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
    public string? NextPlanHandle { get; set; }

    /// <summary>The lifecycle actions that are legal from the current state.</summary>
    public List<string> AllowedActions { get; set; } = new();

    public static SubscriptionDto FromSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State.ToString(),
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PlanPriceInCents = subscription.PlanPriceInCents,
        PlanPrice = subscription.PlanPrice,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        NextPlanHandle = subscription.NextPlanHandle,
        AllowedActions = subscription.AllowedTransitions.Select(action => action.ToString()).ToList()
    };
}

/// <summary>The outcome of a usage report as exposed over the API.</summary>
public class UsageReportDto
{
    public long UsageRecordId { get; set; }
    public int SubscriptionId { get; set; }
    public int ComponentId { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }

    /// <summary>
    /// Units accrued so far this period, or <see langword="null"/> when the running total could
    /// not be read back. The usage itself is recorded either way.
    /// </summary>
    public decimal? PeriodToDateQuantity { get; set; }

    public decimal? PeriodToDateCharge { get; set; }
    public long UnitPriceInCents { get; set; }

    public static UsageReportDto FromReport(UsageReport report) => new()
    {
        UsageRecordId = report.Record.Id,
        SubscriptionId = report.Record.SubscriptionId,
        ComponentId = report.Record.ComponentId,
        Quantity = report.Record.Quantity,
        Memo = report.Record.Memo,
        PeriodToDateQuantity = report.PeriodToDateQuantity,
        PeriodToDateCharge = report.PeriodToDateCharge,
        UnitPriceInCents = report.UnitPriceInCents
    };
}

/// <summary>A previewed plan change as exposed over the API.</summary>
public class PlanChangePreviewDto
{
    public int SubscriptionId { get; set; }
    public string CurrentPlanHandle { get; set; } = string.Empty;
    public string TargetPlanHandle { get; set; } = string.Empty;
    public string Timing { get; set; } = string.Empty;
    public long ProratedAdjustmentInCents { get; set; }
    public long ChargeInCents { get; set; }
    public long CreditAppliedInCents { get; set; }

    /// <summary>
    /// What the customer pays now. Echo this back on the commit call to guarantee the change is
    /// applied at the previewed amount or refused.
    /// </summary>
    public long PaymentDueInCents { get; set; }

    public decimal PaymentDue { get; set; }

    public static PlanChangePreviewDto FromPreview(PlanChangePreview preview) => new()
    {
        SubscriptionId = preview.SubscriptionId,
        CurrentPlanHandle = preview.CurrentPlanHandle,
        TargetPlanHandle = preview.TargetPlanHandle,
        Timing = preview.Timing.ToString(),
        ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents,
        ChargeInCents = preview.ChargeInCents,
        CreditAppliedInCents = preview.CreditAppliedInCents,
        PaymentDueInCents = preview.PaymentDueInCents,
        PaymentDue = preview.PaymentDue
    };
}
