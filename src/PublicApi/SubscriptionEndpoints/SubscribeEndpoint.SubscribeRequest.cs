using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>API handle of the plan (Maxio product) to subscribe to, e.g. from GET /api/subscription-plans.</summary>
    [Required]
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>Optional first name for the Maxio customer record (defaults to a value derived from the user's email).</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional last name for the Maxio customer record.</summary>
    public string? LastName { get; set; }
}
