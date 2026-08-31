using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemDto> Items { get; set; } = new();
    public ShipToAddressDto ShipToAddress { get; set; } = new();
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}
