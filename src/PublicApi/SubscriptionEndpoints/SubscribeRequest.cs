using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Request body for <c>POST /api/subscriptions</c>. The subscriber's identity comes from the JWT.</summary>
public class SubscribeRequest
{
    /// <summary>The handle of the plan to subscribe to (e.g. a product handle from GET /api/subscription-plans).</summary>
    [Required(AllowEmptyStrings = false)]
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Optional given name for the Maxio customer; defaults from the authenticated user when omitted.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional family name for the Maxio customer; defaults when omitted.</summary>
    public string? LastName { get; set; }
}
