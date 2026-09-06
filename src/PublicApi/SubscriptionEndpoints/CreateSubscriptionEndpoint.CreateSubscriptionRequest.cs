using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to, as returned by api/subscription-plans.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Optional non-default price point on the plan.</summary>
    public string? PricePointHandle { get; set; }

    /// <summary>
    /// Optional key that makes a retried signup safe across processes. May also be supplied as an
    /// Idempotency-Key header, which takes precedence.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// The authenticated shopper. Always taken from the bearer token, never from the request body.
    /// </summary>
    [JsonIgnore]
    public string UserName { get; internal set; } = string.Empty;

    /// <summary>
    /// The shopper's email address, resolved from their eShopOnWeb account. Never taken from the
    /// request body: it becomes the contact address on the billing customer.
    /// </summary>
    [JsonIgnore]
    public string Email { get; internal set; } = string.Empty;
}
