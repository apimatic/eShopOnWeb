using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Stable handle of the plan (Maxio product) to subscribe to, e.g. "eshop-pro".</summary>
    public string ProductHandle { get; set; } = string.Empty;
}

public sealed class SubscriptionItemDto
{
    public int MaxioSubscriptionId { get; set; }
    public string MaxioReference { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public SubscriptionItemDto? Subscription { get; set; }
}

public sealed class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId) { }

    public List<SubscriptionItemDto> Subscriptions { get; set; } = new();
}
