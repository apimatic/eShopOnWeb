using System;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateShopperSubscriptionResponse : BaseResponse
{
    public CreateShopperSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateShopperSubscriptionResponse()
    {
    }

    public bool Created { get; set; }

    public ShopperSubscriptionDto Subscription { get; set; } = new();

    internal static CreateShopperSubscriptionResponse From(SubscriptionEnrollment enrollment, Guid? correlationId = null)
    {
        var response = correlationId is Guid id
            ? new CreateShopperSubscriptionResponse(id)
            : new CreateShopperSubscriptionResponse();
        response.Created = enrollment.Created;
        response.Subscription = SubscriptionMapper.Map(enrollment.Subscription);
        return response;
    }
}
