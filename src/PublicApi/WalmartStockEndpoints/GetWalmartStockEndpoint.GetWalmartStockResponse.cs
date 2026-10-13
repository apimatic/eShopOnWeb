using System;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

public class GetWalmartStockResponse : BaseResponse
{
    public GetWalmartStockResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetWalmartStockResponse()
    {
    }

    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }
}
