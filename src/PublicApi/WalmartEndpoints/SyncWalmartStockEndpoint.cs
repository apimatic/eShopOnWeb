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
/// Operator endpoint: POST /api/walmart/stock/sync
/// Pushes the eShop stock level of every catalog item to Walmart in one call (an item whose level
/// was never set counts as 0). Reports, per SKU, what Walmart did with it.
/// </summary>
public class SyncWalmartStockEndpoint : IEndpoint<IResult, IWalmartStockService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/walmart/stock/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IWalmartStockService stockService, CancellationToken ct) =>
            {
                return await HandleAsync(stockService);
            })
            .Produces<SyncWalmartStockResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    public async Task<IResult> HandleAsync(IWalmartStockService stockService)
    {
        var results = await stockService.SyncCatalogToWalmartAsync();

        var response = new SyncWalmartStockResponse
        {
            Results = results
                .Select(r => new SyncResultDto
                {
                    Sku = r.Sku,
                    Status = r.Status == WalmartSyncStatus.Updated ? "updated" : "failed"
                })
                .ToList()
        };
        return Results.Ok(response);
    }
}

public class SyncWalmartStockResponse
{
    public List<SyncResultDto> Results { get; set; } = new();
}

public class SyncResultDto
{
    public string Sku { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
