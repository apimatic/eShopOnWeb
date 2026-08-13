using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }

    public PlaceOrderResponse() { }

    /// <summary>Identifier of the placed order, returned as a top-level field so the flow can be driven on.</summary>
    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;

    public decimal Total { get; set; }
}
