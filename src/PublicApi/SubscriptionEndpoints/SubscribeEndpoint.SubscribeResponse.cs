using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    public int? SubscriptionId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }

    /// <summary>Set only on 202 (state "Unknown"): the billing provider write may or may not have landed; repeat the request to settle it.</summary>
    public string? Message { get; set; }

    public static SubscribeResponse From(Guid correlationId, SubscriptionDto dto) => new(correlationId)
    {
        SubscriptionId = dto.SubscriptionId,
        PlanHandle = dto.PlanHandle,
        PlanName = dto.PlanName,
        PriceInCents = dto.PriceInCents,
        Interval = dto.Interval,
        IntervalUnit = dto.IntervalUnit,
        State = dto.State,
        NextBillingAt = dto.NextBillingAt
    };
}
