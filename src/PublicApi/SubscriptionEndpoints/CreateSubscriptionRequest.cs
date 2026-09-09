using System;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated user in a subscription plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to. When omitted, the configured default plan is used.
    /// </summary>
    public string? PlanHandle { get; set; }
}

public class SubscriptionDto
{
    public long MaxioSubscriptionId { get; set; }

    public long MaxioCustomerId { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public string? Currency { get; set; }

    /// <summary>Date the next billing event is assessed.</summary>
    public DateTime? NextBillingDate { get; set; }

    public DateTime? CreatedAt { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDto Subscription { get; set; } = new SubscriptionDto();

    /// <summary>True when the user already held a live subscription for the plan (idempotent replay).</summary>
    public bool AlreadySubscribed { get; set; }
}
