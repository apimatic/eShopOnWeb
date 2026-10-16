using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
    public bool Truncated { get; set; }
}

public class SubscribeRequest : BaseRequest
{
    public string? ProductHandle { get; set; }
}

public class ShopperSubscriptionDto
{
    public int? Id { get; set; }
    public string? Reference { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public long? PriceInCents { get; set; }
    public string? Currency { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
}

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ShopperSubscriptionDto? Subscription { get; set; }
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public List<ShopperSubscriptionDto> Subscriptions { get; set; } = new();
}

internal static class SubscriptionResults
{
    public static IResult FromBilling(SubscriptionBillingException exception) =>
        Results.Json(new { message = exception.Message }, statusCode: exception.StatusCode);
}
