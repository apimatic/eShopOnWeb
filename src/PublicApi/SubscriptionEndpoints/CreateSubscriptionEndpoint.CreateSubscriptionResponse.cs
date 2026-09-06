using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>The live subscription, whether it was created by this call or already existed.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// False when the shopper was already enrolled and this call changed nothing - the response
    /// is 200 rather than 201 in that case.
    /// </summary>
    public bool Created { get; set; }

    /// <summary>Confirmation summarising the plan, price, state and next billing date.</summary>
    public string Message { get; set; } = string.Empty;
}
