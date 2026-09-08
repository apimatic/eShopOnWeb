namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request body for <c>POST /api/subscriptions</c>.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The API handle of the plan to subscribe to (see <c>GET /api/subscription-plans</c>).</summary>
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>Optional billing first name for the Maxio customer record.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional billing last name for the Maxio customer record.</summary>
    public string? LastName { get; set; }
}
