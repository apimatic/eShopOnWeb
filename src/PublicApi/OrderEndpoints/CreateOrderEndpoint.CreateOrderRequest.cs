using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineInput>? Items { get; set; }
    public ShipToAddressInput? ShipToAddress { get; set; }

    /// <summary>Set from the caller's token, never from the body.</summary>
    [JsonIgnore]
    public string? BuyerId { get; set; }
}

public class OrderLineInput
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressInput
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateOrderResponse()
    {
    }

    public int OrderId { get; set; }
    public OrderDto? Order { get; set; }
}
