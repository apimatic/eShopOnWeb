using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body for <c>POST /api/subscriptions</c>. Only the plan (and optional name fields) come
/// from the payload; the customer identity is taken from the authenticated token and set
/// server-side, so it cannot be spoofed via the body.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Product handle of the plan to subscribe to (e.g. "eshop-pro").</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Optional first name for the Maxio customer; derived from the email when omitted.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional last name for the Maxio customer; a default is used when omitted.</summary>
    public string? LastName { get; set; }

    /// <summary>Customer reference (the eShopOnWeb user identity). Set from the token, never bound from the body.</summary>
    [JsonIgnore]
    public string UserReference { get; private set; } = string.Empty;

    /// <summary>Customer email. Set from the token, never bound from the body.</summary>
    [JsonIgnore]
    public string Email { get; private set; } = string.Empty;

    public void SetIdentity(string userReference, string email)
    {
        UserReference = userReference;
        Email = email;
    }
}
