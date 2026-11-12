using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Billing;

namespace Microsoft.eShopWeb.PublicApi.PlanEndpoints;

public class ListPlansResponse : BaseResponse
{
    public ListPlansResponse(System.Guid correlationId) : base(correlationId) { }
    public ListPlansResponse() { }
    public List<PlanDto> Plans { get; set; } = new();
}

public class CreatePlanRequest
{
    public string? Name { get; set; }
    public decimal? Price { get; set; }
    public string? Brand { get; set; }
    public int? CatalogBrandId { get; set; }
}

public class CreateCurrencyPriceRequest
{
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}
