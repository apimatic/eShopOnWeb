using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by GET /api/subscription-plans - for
    /// example "eshop-pro". Required.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional given name for the billing customer record. Only used the first time a billing
    /// customer is created for this shopper; otherwise a name is derived from their email.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>Optional family name for the billing customer record. See <see cref="FirstName"/>.</summary>
    public string? LastName { get; set; }

    /// <summary>
    /// Optional idempotency key. May also be supplied as the <c>Idempotency-Key</c> request
    /// header. Retrying with the same key cannot create a second subscription. Omitting it is
    /// still safe: the endpoint reconciles against the billing provider before enrolling.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// The shopper this request acts for. Resolved from the bearer token by the endpoint and
    /// never bound from the request body, so a caller cannot subscribe on someone else's behalf.
    /// </summary>
    [JsonIgnore]
    internal SubscriberIdentity? Subscriber { get; set; }
}
