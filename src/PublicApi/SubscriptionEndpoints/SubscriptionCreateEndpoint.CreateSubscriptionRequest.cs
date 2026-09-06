using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional given name for the billing record. eShopOnWeb identities carry no name, so when this
    /// is omitted one is derived from the caller's e-mail address.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>Optional family name for the billing record. Derived from the e-mail when omitted.</summary>
    public string? LastName { get; set; }

    /// <summary>
    /// Optional key that scopes replay protection for this request. Omit it and the (caller, plan)
    /// pair is used instead, so a double-click still resolves to a single subscription.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// The authenticated caller, taken from the bearer token in the route handler.
    /// [JsonIgnore] keeps it out of model binding, so a request body can never assert an identity.
    /// </summary>
    [JsonIgnore]
    public string? UserName { get; set; }
}
