using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of POST /api/subscriptions. The shopper is identified by the bearer token, so the
/// request only says which plan to join.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to, e.g. "eshop-pro".</summary>
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Optional. Used only if a billing customer has to be created for this shopper.</summary>
    [StringLength(100)]
    public string? FirstName { get; set; }

    /// <summary>Optional. Used only if a billing customer has to be created for this shopper.</summary>
    [StringLength(100)]
    public string? LastName { get; set; }

    /// <summary>
    /// Optional. Repeating a request with the same key is guaranteed to yield the same
    /// subscription rather than a second one, even across application instances.
    /// </summary>
    [StringLength(128)]
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// The shopper acting. Filled in from the bearer token by the endpoint and ignored on
    /// the wire, so a caller cannot subscribe somebody else.
    /// </summary>
    [JsonIgnore]
    public string? UserName { get; set; }
}
