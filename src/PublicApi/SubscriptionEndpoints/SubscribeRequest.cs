using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request body for POST /api/subscriptions. Only <see cref="PlanHandle"/> is
/// supplied by the caller; the subscriber identity is resolved server-side from the
/// bearer token and is never accepted from the client.
/// </summary>
public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (e.g. "eshop-pro").</summary>
    public string? PlanHandle { get; set; }

    // ---- Server-populated identity (not bound from the request body) ----

    [JsonIgnore]
    public string? UserReference { get; private set; }

    [JsonIgnore]
    public string? Email { get; private set; }

    [JsonIgnore]
    public string? FirstName { get; private set; }

    [JsonIgnore]
    public string? LastName { get; private set; }

    public void SetSubscriber(SubscriberIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        UserReference = identity.Reference;
        Email = identity.Email;
        FirstName = identity.FirstName;
        LastName = identity.LastName;
    }
}
