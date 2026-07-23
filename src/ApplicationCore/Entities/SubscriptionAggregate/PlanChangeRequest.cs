using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>When a plan change takes effect (plan.md UC3, step 1).</summary>
public enum PlanChangeTiming
{
    /// <summary>Apply now and charge or credit the prorated difference.</summary>
    Immediately = 0,

    /// <summary>Apply at the next renewal; no proration is charged.</summary>
    AtNextRenewal = 1
}

/// <summary>
/// A request to move a subscription to another plan.
/// </summary>
/// <remarks>
/// For <see cref="PlanChangeTiming.Immediately"/>, the amount the customer was shown must be echoed back
/// in <see cref="ConfirmedPaymentDueInCents"/> together with <see cref="PreviewedAt"/>. The service
/// re-previews at commit time and refuses to apply a different amount than the one confirmed
/// (plan.md UC3, "preview is stale at commit time").
/// </remarks>
public sealed record PlanChangeRequest
{
    public required string TargetPlanHandle { get; init; }

    public required PlanChangeTiming Timing { get; init; }

    /// <summary>The net amount due the customer confirmed, in minor units (cents).</summary>
    public long? ConfirmedPaymentDueInCents { get; init; }

    /// <summary>When the confirmed preview was produced.</summary>
    public DateTimeOffset? PreviewedAt { get; init; }

    /// <summary>Builds a commit request from a preview the customer accepted verbatim.</summary>
    public static PlanChangeRequest FromConfirmedPreview(PlanChangePreview preview) => new()
    {
        TargetPlanHandle = preview.TargetPlanHandle,
        Timing = PlanChangeTiming.Immediately,
        ConfirmedPaymentDueInCents = preview.PaymentDueInCents,
        PreviewedAt = preview.PreviewedAt
    };

    /// <summary>Builds a commit request that defers the change to the next renewal (no proration).</summary>
    public static PlanChangeRequest AtNextRenewalFor(string targetPlanHandle) => new()
    {
        TargetPlanHandle = targetPlanHandle,
        Timing = PlanChangeTiming.AtNextRenewal
    };
}
