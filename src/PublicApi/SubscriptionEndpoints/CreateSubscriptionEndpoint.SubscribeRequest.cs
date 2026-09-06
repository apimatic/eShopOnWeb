using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of POST /api/subscriptions.
/// </summary>
public class SubscribeApiRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by GET /api/subscription-plans. Required.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>Optional. Defaults to a name derived from the account's email address.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional. Defaults to a name derived from the account's email address.</summary>
    public string? LastName { get; set; }

    /// <summary>Optional company name to record on the billing customer.</summary>
    public string? Organization { get; set; }

    /// <summary>
    /// Optional. Repeat the same key when retrying a request you are not sure completed, and the
    /// billing system will refuse to create a second subscription for it.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// The subscriber. Populated from the bearer token by the endpoint and deliberately not part of
    /// the request body, so a caller cannot subscribe somebody else.
    /// </summary>
    [JsonIgnore]
    public string? UserName { get; set; }
}
