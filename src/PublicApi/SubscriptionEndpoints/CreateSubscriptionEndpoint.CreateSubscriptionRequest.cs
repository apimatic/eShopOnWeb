using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The handle of the product (plan) to subscribe to.
    /// </summary>
    [Required]
    public string ProductHandle { get; set; } = string.Empty;
}
