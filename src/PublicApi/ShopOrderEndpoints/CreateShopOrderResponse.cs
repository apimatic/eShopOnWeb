using System;

namespace Microsoft.eShopWeb.PublicApi.ShopOrderEndpoints;

public class CreateShopOrderResponse : BaseResponse
{
    public CreateShopOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateShopOrderResponse()
    {
    }

    public int OrderId { get; set; }
}
