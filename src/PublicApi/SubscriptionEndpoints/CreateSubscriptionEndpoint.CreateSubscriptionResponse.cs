using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public string ProductHandle { get; set; }
    public string ProductName { get; set; }
    public decimal Price { get; set; }
    public string State { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public UserSubscriptionDto Subscription { get; set; }
    public bool Created { get; set; }
}
