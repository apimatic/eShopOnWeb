using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (see GET api/subscription-plans).
    /// </summary>
    [Required]
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional billing first name; defaults to a value derived from the shopper's email.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// Optional billing last name; defaults to a value derived from the shopper's email.
    /// </summary>
    public string? LastName { get; set; }
}
