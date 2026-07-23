using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    /// <summary>One of <c>Pause</c>, <c>Resume</c>, <c>Cancel</c>, <c>Reactivate</c>.</summary>
    [Required]
    public string Action { get; set; } = string.Empty;

    /// <summary>For <c>Cancel</c> only: <c>Immediate</c> or <c>EndOfPeriod</c>. Defaults to immediate.</summary>
    public string CancellationTiming { get; set; } = ImmediateCancellation;

    private const string ImmediateCancellation = "Immediate";

    public string? Reason { get; set; }

    /// <summary>Taken from the route, not the body.</summary>
    [JsonIgnore]
    public int SubscriptionId { get; set; }

    /// <summary>Taken from the access token: null for administrators, the caller otherwise.</summary>
    [JsonIgnore]
    public string? OwnerReference { get; set; }

    public SubscriptionLifecycleAction ResolveAction() =>
        Enum.TryParse<SubscriptionLifecycleAction>(Action, ignoreCase: true, out var action)
            ? action
            : throw new InvalidSubscriptionOperationException(
                $"'{Action}' is not a valid lifecycle action. Use Pause, Resume, Cancel or Reactivate.");

    public CancellationTiming ResolveCancellationTiming() =>
        Enum.TryParse<CancellationTiming>(CancellationTiming, ignoreCase: true, out var timing)
            ? timing
            : throw new InvalidSubscriptionOperationException(
                $"'{CancellationTiming}' is not a valid cancellation timing. Use Immediate or EndOfPeriod.");
}
