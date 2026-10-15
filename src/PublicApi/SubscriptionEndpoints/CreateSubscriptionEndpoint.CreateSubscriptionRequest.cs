using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of a plan returned by <c>GET /api/subscription-plans</c>.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Optional first name for the billing account (defaults to the email's local part).</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional last name for the billing account.</summary>
    public string? LastName { get; set; }

    /// <summary>The caller, taken from the bearer token — never from the request body.</summary>
    [JsonIgnore]
    public BuyerIdentity? Buyer { get; set; }
}
