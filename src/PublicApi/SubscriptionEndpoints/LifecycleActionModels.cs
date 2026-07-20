using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Shared request shape for the pause/resume/reactivate lifecycle actions (no extra input beyond the route id).</summary>
public class LifecycleActionRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string? OwnerReference { get; set; }
}

public class LifecycleActionResponse : BaseResponse
{
    public LifecycleActionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public LifecycleActionResponse()
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();
}
