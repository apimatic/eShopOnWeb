using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.WalmartIntegration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

/// <summary>
/// POST /api/walmart/stock/sync — operator action: pushes the eShop stock level of every catalog
/// item to Walmart, reporting per SKU what Walmart did. Admin only.
/// </summary>
public class SyncWalmartStockEndpoint : IEndpoint<IResult, IWalmartInventoryService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/walmart/stock/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IWalmartInventoryService walmart, CancellationToken ct) =>
            {
                return await HandleAsync(walmart, ct);
            })
            .Produces<SyncWalmartStockResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    public Task<IResult> HandleAsync(IWalmartInventoryService walmart) => HandleAsync(walmart, default);

    public async Task<IResult> HandleAsync(IWalmartInventoryService walmart, CancellationToken ct)
    {
        var results = await walmart.SyncCatalogAsync(ct);

        return Results.Ok(new SyncWalmartStockResponse
        {
            Results = results
                .Select(r => new SyncResultItem { Sku = r.Sku, Status = r.Status })
                .ToList(),
        });
    }
}
