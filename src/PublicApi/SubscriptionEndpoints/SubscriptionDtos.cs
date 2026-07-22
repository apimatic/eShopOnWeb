using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Wire representation of a subscribable plan (UC1).</summary>
public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Interval { get; set; } = string.Empty;
    public int IntervalCount { get; set; }
    public bool RequiresPaymentMethod { get; set; }
}

/// <summary>Wire representation of a customer's subscription.</summary>
public class CustomerSubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal ProductPrice { get; set; }
    public string Interval { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>Wire representation of a usage recording result (UC2).</summary>
public class UsageResultDto
{
    public int RecordedQuantity { get; set; }
    public decimal? PeriodToDateTotal { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? EstimatedPeriodCharge { get; set; }
}

/// <summary>Wire representation of a plan-change preview (UC3). Echoed back on commit for staleness checks.</summary>
public class PlanChangePreviewDto
{
    public string TargetProductHandle { get; set; } = string.Empty;
    public bool ApplyImmediately { get; set; }
    public decimal ProratedAdjustment { get; set; }
    public decimal ChargeAmount { get; set; }
    public decimal PaymentDue { get; set; }
    public decimal CreditApplied { get; set; }
}

/// <summary>Maps the ApplicationCore domain models onto the PublicApi wire DTOs.</summary>
public static class SubscriptionDtoMapper
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalCount = plan.IntervalCount,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        ProductPrice = subscription.ProductPrice,
        Interval = subscription.Interval,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        IsActive = subscription.IsActive
    };

    public static UsageResultDto ToDto(this UsageResult usage) => new()
    {
        RecordedQuantity = usage.RecordedQuantity,
        PeriodToDateTotal = usage.PeriodToDateTotal,
        UnitPrice = usage.UnitPrice,
        EstimatedPeriodCharge = usage.EstimatedPeriodCharge
    };

    public static PlanChangePreviewDto ToDto(this PlanChangePreview preview) => new()
    {
        TargetProductHandle = preview.TargetProductHandle,
        ApplyImmediately = preview.ApplyImmediately,
        ProratedAdjustment = preview.ProratedAdjustment,
        ChargeAmount = preview.ChargeAmount,
        PaymentDue = preview.PaymentDue,
        CreditApplied = preview.CreditApplied
    };

    public static PlanChangePreview ToDomain(this PlanChangePreviewDto dto) => new(
        dto.TargetProductHandle,
        dto.ApplyImmediately,
        dto.ProratedAdjustment,
        dto.ChargeAmount,
        dto.PaymentDue,
        dto.CreditApplied);
}
