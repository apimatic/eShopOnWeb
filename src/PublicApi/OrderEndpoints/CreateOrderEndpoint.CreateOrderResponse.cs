using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Response for a placed order. <see cref="OrderId"/> is the top-level identifier of the new order.</summary>
public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int ItemCount { get; set; }
}
