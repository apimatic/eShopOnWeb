using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Response for <c>GET /api/subscription-plans</c>.</summary>
public class SubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

/// <summary>Response for <c>GET /api/my-subscriptions</c>.</summary>
public class MySubscriptionsResponse
{
    public List<CustomerSubscriptionDto> Subscriptions { get; set; } = new();
}

/// <summary>Response for <c>POST /api/subscriptions</c>.</summary>
public class SubscribeResponse
{
    public CustomerSubscriptionDto Subscription { get; set; } = new();
}

/// <summary>Error payload returned when a billing operation fails.</summary>
public class SubscriptionErrorResponse
{
    public string Message { get; set; } = string.Empty;

    /// <summary>Provider-supplied validation messages, when the failure was a validation rejection.</summary>
    public List<string> Errors { get; set; } = new();
}
