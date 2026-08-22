namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public System.Collections.Generic.List<CreateOrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipTo { get; set; }
}

public class CreateOrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}
