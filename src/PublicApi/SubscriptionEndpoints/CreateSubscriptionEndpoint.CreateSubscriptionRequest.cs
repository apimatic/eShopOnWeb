using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (see GET /api/subscription-plans).</summary>
    [Required]
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Optional billing first name for the Maxio customer record; derived from the account email when omitted.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional billing last name for the Maxio customer record.</summary>
    public string? LastName { get; set; }

    /// <summary>The authenticated user's username, populated from the JWT — never from the client.</summary>
    public string Username { get; set; } = string.Empty;
}
