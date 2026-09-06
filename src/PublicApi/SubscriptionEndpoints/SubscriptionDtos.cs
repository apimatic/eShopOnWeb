using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long BalanceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public long ProductPriceInCents { get; set; }
}

public class CreateSubscriptionRequestDto : BaseRequest
{
    public int ProductId { get; set; }
    public string? ProductHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public SubscriptionDto? Subscription { get; set; }
    public bool Created { get; set; }
}

public class SubscriptionListResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

public class ProductListResponse : BaseResponse
{
    public List<ProductDto> Products { get; set; } = new();
}
