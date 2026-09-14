using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanResponse(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record SubscriptionResponse(
    int Id,
    string Reference,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string? Currency,
    string? State,
    DateTimeOffset? NextBillingDate,
    int? Interval,
    string? IntervalUnit);

public sealed class CreateSubscriptionRequest
{
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string ProductHandle { get; set; } = string.Empty;
}
