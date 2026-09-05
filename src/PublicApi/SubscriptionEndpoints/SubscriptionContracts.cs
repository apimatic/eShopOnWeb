using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscribeRequest
{
    [Required]
    [StringLength(255)]
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed record SubscriptionPlanResponse(string Handle, string Name, string? Description, long PriceInCents, int Interval, string IntervalUnit, bool RequiresPaymentMethod);
public sealed record SubscriptionResponse(int Id, string PlanHandle, string PlanName, long PriceInCents, string State, DateTimeOffset? NextBillingAt);
public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionResponse> Subscriptions);
