using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The quoted cost of a plan change. The quote is repeated at the top level so a caller can confirm
/// the change by echoing these fields straight back to the plan-change endpoint.
/// </summary>
public class PlanChangePreviewResponse : BaseResponse
{
    public PlanChangePreviewResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PlanChangePreviewResponse()
    {
    }

    public PlanChangePreviewDto? Preview { get; set; }

    /// <summary>The plan the subscription is on today.</summary>
    public string CurrentPlanHandle { get; set; } = string.Empty;

    /// <summary>The plan the quote is for. Echo this back to commit the change.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>The plan the quote is for.</summary>
    public string TargetPlanHandle { get; set; } = string.Empty;

    /// <summary><c>Immediate</c> or <c>NextRenewal</c>.</summary>
    public string Timing { get; set; } = string.Empty;

    public decimal ProratedAdjustment { get; set; }
    public decimal Charge { get; set; }

    /// <summary>What the customer will be charged now. Echo this back as the previewed payment due.</summary>
    public decimal PaymentDue { get; set; }

    public decimal CreditApplied { get; set; }
    public decimal TargetPlanPrice { get; set; }

    public long ProratedAdjustmentInCents { get; set; }
    public long ChargeInCents { get; set; }

    /// <summary><see cref="PaymentDue"/> in the minor units the provider reports.</summary>
    public long AmountDueInCents { get; set; }

    /// <summary>The same figure as <see cref="AmountDueInCents"/>, under the payment-due name.</summary>
    public long PaymentDueInCents { get; set; }

    public long CreditAppliedInCents { get; set; }
    public long TargetPlanPriceInCents { get; set; }

    public static PlanChangePreviewResponse From(Guid correlationId, PlanChangePreviewDto preview) => new(correlationId)
    {
        ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents,
        ChargeInCents = preview.ChargeInCents,
        AmountDueInCents = preview.AmountDueInCents,
        PaymentDueInCents = preview.PaymentDueInCents,
        CreditAppliedInCents = preview.CreditAppliedInCents,
        TargetPlanPriceInCents = preview.TargetPlanPriceInCents,
        Preview = preview,
        CurrentPlanHandle = preview.CurrentPlanHandle,
        PlanHandle = preview.TargetPlanHandle,
        TargetPlanHandle = preview.TargetPlanHandle,
        Timing = preview.Timing,
        ProratedAdjustment = preview.ProratedAdjustment,
        Charge = preview.Charge,
        PaymentDue = preview.PaymentDue,
        CreditApplied = preview.CreditApplied,
        TargetPlanPrice = preview.TargetPlanPrice
    };
}
