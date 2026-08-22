using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PlaceOrderResponse()
    {
    }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}
