using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse() { }

    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;

    public decimal Total { get; set; }

    public string Currency { get; set; } = string.Empty;

    public DateTimeOffset OrderDate { get; set; }
}
