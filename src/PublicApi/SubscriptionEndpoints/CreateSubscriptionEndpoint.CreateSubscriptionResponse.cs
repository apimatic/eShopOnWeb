using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    /// <summary>Maxio subscription id.</summary>
    public long SubscriptionId { get; set; }

    public string Reference { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring price per billing period.</summary>
    public decimal Price { get; set; }

    /// <summary>Subscription state as recorded in Maxio (active, trialing, ...).</summary>
    public string State { get; set; } = string.Empty;

    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>True when a new subscription was created; false when an existing one was returned (idempotent replay).</summary>
    public bool Created { get; set; }

    public SubscriptionDto Subscription { get; set; } = new();
}