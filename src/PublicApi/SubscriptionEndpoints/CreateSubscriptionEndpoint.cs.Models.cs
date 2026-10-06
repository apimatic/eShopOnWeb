using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated user to a plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The Maxio plan (product) handle to subscribe to, e.g. the handle from
    /// GET /api/subscription-plans. When omitted, the first plan in the
    /// configured product family is used.
    /// </summary>
    [JsonPropertyName("planHandle")]
    public string? PlanHandle { get; set; }
}

/// <summary>
/// Response confirming the subscription created (or already active) for the user.
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse() { }

    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>
    /// The Maxio subscription id.
    /// </summary>
    public int SubscriptionId { get; set; }

    /// <summary>
    /// True when the user was already subscribed to this plan and nothing new was created.
    /// </summary>
    public bool AlreadySubscribed { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public long PriceInCents { get; set; }

    /// <summary>
    /// The recurring price formatted as a decimal string, e.g. "299.00".
    /// </summary>
    public string Price { get; set; } = string.Empty;

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio subscription state, e.g. "active".
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// When the next billing event is scheduled.
    /// </summary>
    public DateTime? NextBillingDate { get; set; }

    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// The Maxio customer id linked to the user.
    /// </summary>
    public int MaxioCustomerId { get; set; }
}