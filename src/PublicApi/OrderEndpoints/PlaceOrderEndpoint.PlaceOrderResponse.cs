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

    /// <summary>The identifier of the placed order.</summary>
    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}
