using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>API handle of the plan (product) to subscribe to, e.g. "eshop-pro".</summary>
    [Required]
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Optional first name for the billing customer record; derived from the account when omitted.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional last name for the billing customer record; derived from the account when omitted.</summary>
    public string? LastName { get; set; }
}
