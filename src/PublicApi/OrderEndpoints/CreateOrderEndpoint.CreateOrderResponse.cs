using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateOrderResponse()
    {
    }

    /// <summary>The identifier of the placed order — used to raise a bill against it.</summary>
    public int OrderId { get; set; }

    public string BuyerId { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<CreateOrderResponseItem> Items { get; set; } = new();
}

public class CreateOrderResponseItem
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
