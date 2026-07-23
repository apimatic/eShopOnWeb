using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The body shared by the plan-change preview and commit endpoints (UC3).
/// </summary>
public class PlanChangeRequest : BaseRequest
{
    /// <summary>The handle of the plan to move to.</summary>
    [Required]
    public string TargetPlanHandle { get; set; } = string.Empty;

    /// <summary><c>Immediate</c> (prorated now) or <c>AtNextRenewal</c> (deferred, not prorated).</summary>
    public string Timing { get; set; } = nameof(PlanChangeTiming.Immediate);

    /// <summary>
    /// The <c>Token</c> from the preview the customer confirmed. Required by the commit endpoint and
    /// ignored by the preview endpoint.
    /// </summary>
    public string? PreviewToken { get; set; }

    /// <summary>Taken from the route, not the body.</summary>
    [JsonIgnore]
    public int SubscriptionId { get; set; }

    /// <summary>Taken from the access token: null for administrators, the caller otherwise.</summary>
    [JsonIgnore]
    public string? OwnerReference { get; set; }

    public PlanChangeTiming ResolveTiming() =>
        Enum.TryParse<PlanChangeTiming>(Timing, ignoreCase: true, out var timing)
            ? timing
            : throw new InvalidSubscriptionOperationException(
                $"'{Timing}' is not a valid plan-change timing. Use '{nameof(PlanChangeTiming.Immediate)}' or '{nameof(PlanChangeTiming.AtNextRenewal)}'.");
}
