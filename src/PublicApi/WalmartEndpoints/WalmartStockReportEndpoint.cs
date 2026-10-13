using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Walmart;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WalmartEndpoints;

/// <summary>
/// Operator endpoint: GET /api/walmart/stock
/// Lists every SKU Walmart holds stock for, lined up against eShop's own level for the matching
/// catalog item, so SKUs whose two numbers differ — or that exist on only one side — are visible.
/// </summary>
public class WalmartStockReportEndpoint : IEndpoint<IResult, IWalmartStockService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/walmart/stock",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IWalmartStockService stockService, CancellationToken ct) =>
            {
                return await HandleAsync(stockService);
            })
            .Produces<WalmartStockReportResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    public async Task<IResult> HandleAsync(IWalmartStockService stockService)
    {
        var items = await stockService.ReconcileAsync();

        var response = new WalmartStockReportResponse
        {
            Items = items
                .Select(i => new WalmartStockReportItemDto
                {
                    Sku = i.Sku,
                    WalmartQuantity = i.WalmartQuantity,
                    EshopQuantity = i.EshopQuantity
                })
                .ToList()
        };
        return Results.Ok(response);
    }
}

public class WalmartStockReportResponse
{
    public List<WalmartStockReportItemDto> Items { get; set; } = new();
}

public class WalmartStockReportItemDto
{
    public string Sku { get; set; } = string.Empty;
    public int? WalmartQuantity { get; set; }
    public int? EshopQuantity { get; set; }
}
