using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    public CreateSubscriptionRequest(Guid correlationId) : base()
    {
        base._correlationId = correlationId;
    }

    public CreateSubscriptionRequest()
    {
    }

    /// <summary>
    /// Handle of the plan to subscribe to, e.g. "eshop-pro".
    /// </summary>
    [Required]
    public string ProductHandle { get; set; } = string.Empty;
}
