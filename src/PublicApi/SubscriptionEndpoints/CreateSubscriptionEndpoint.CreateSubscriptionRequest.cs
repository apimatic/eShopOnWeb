using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of <c>POST /api/subscriptions</c>.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to, as listed by <c>GET /api/subscription-plans</c>.</summary>
    [Required]
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional key that makes the request replayable. Sending the same key again for the same
    /// shopper and plan returns the subscription the first call produced. May also be supplied with
    /// the <c>Idempotency-Key</c> request header.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Optional billing contact details. Only cosmetic fields are accepted; the identity being
    /// billed always comes from the bearer token.
    /// </summary>
    public SubscriberDetails? Customer { get; set; }

    /// <summary>
    /// The authenticated shopper, resolved from the bearer token by the route handler. Not part of
    /// the request body and never bound from it.
    /// </summary>
    [JsonIgnore]
    internal SubscriberProfile? Subscriber { get; set; }

    /// <summary>Cancellation of the inbound HTTP request, attached by the route handler.</summary>
    [JsonIgnore]
    internal CancellationToken CancellationToken { get; set; }
}

/// <summary>
/// Optional billing contact details supplied by the caller.
/// </summary>
public class SubscriberDetails
{
    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    [MaxLength(200)]
    public string? Organization { get; set; }
}
