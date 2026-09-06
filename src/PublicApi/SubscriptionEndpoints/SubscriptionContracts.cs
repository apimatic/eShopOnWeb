using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanResponse(string Handle, string Name, long PriceInCents, int Interval, string IntervalUnit);
public sealed record SubscriptionResponse(long Id, string PlanHandle, string PlanName, long PriceInCents, string State, DateTimeOffset? NextBillingAt);

public sealed class CreateSubscriptionRequest
{
    public string PlanHandle { get; init; } = string.Empty;
}
