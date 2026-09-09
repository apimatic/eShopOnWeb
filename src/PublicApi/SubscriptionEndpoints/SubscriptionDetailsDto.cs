using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The confirmed state of a Maxio subscription, as reported back to the user.
/// </summary>
public class SubscriptionDetailsDto
{
    public long MaxioSubscriptionId { get; set; }
    public long MaxioCustomerId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDateUtc { get; set; }
    public DateTimeOffset? CreatedAtUtc { get; set; }

    /// <summary>
    /// True when the user was already subscribed to this plan and no new
    /// Maxio subscription was created (idempotent double-click).
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

public class CreateSubscriptionResponse
{
    public CreateSubscriptionResponse()
    {
        Subscriptions = new List<SubscriptionDetailsDto>();
    }

    public CreateSubscriptionResponse(Guid correlationId) : this()
    {
        CorrelationId = correlationId;
    }

    public Guid CorrelationId { get; set; }
    public List<SubscriptionDetailsDto> Subscriptions { get; set; }
}

public class GetMySubscriptionsResponse
{
    public GetMySubscriptionsResponse()
    {
        Subscriptions = new List<SubscriptionDetailsDto>();
    }

    public GetMySubscriptionsResponse(Guid correlationId) : this()
    {
        CorrelationId = correlationId;
    }

    public Guid CorrelationId { get; set; }
    public List<SubscriptionDetailsDto> Subscriptions { get; set; }
}
