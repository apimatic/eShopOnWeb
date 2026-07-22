using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Shared by the plan-change preview and commit endpoints (UC3).</summary>
public class PlanChangeRequest : BaseRequest
{
    /// <summary>Taken from the route, never from the request body.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>The plan to move to, e.g. <c>basic-plan</c>.</summary>
    public string TargetPlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// <c>Immediately</c> (prorated) or <c>AtNextRenewal</c> (not prorated). Defaults to
    /// <see cref="PlanChangeTiming.Immediately"/> when omitted.
    /// </summary>
    public string? Timing { get; set; }

    /// <summary>
    /// The amounts the customer was shown. When supplied, the commit is refused unless the
    /// provider still quotes the same figures (UC3 failure scenario).
    /// </summary>
    public PlanChangePreviewDto? ConfirmedPreview { get; set; }

    public PlanChangeTiming ResolveTiming() =>
        Enum.TryParse<PlanChangeTiming>(Timing, ignoreCase: true, out var timing)
            ? timing
            : PlanChangeTiming.Immediately;
}
