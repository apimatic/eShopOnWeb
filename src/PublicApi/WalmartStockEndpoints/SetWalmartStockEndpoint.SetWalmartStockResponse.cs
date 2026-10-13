using System;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

public class SetWalmartStockResponse : BaseResponse
{
    public SetWalmartStockResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SetWalmartStockResponse()
    {
    }

    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }
}
