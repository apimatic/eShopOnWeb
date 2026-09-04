using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequiresPaymentMethod { get; set; }
    public bool Taxable { get; set; }
}

public sealed class SubscriptionDto
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
}

public sealed class SubscribeRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public sealed class SubscriptionPlansResponse : BaseResponse
{
    public SubscriptionPlansResponse() : base()
    {
    }

    public SubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public sealed class SubscribeResponse : BaseResponse
{
    public SubscribeResponse() : base()
    {
    }

    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();
}

public sealed class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse() : base()
    {
    }

    public MySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
