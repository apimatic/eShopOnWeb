using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime ActivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse() : base()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}

public class GetSubscriptionsResponse : BaseResponse
{
    public GetSubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetSubscriptionsResponse() : base()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

public class ErrorResponse : BaseResponse
{
    public ErrorResponse(Guid correlationId, string message) : base(correlationId)
    {
        Message = message;
    }

    public string Message { get; set; } = string.Empty;
}
