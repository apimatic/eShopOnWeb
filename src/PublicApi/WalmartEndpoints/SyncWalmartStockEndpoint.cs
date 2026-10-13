using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Walmart;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WalmartEndpoints;

/// <summary>
/// Pushes the eShop stock level of every catalog item to Walmart (one request from the caller).
/// Reports, per SKU, what Walmart did with it (updated|failed). Operator action.
/// </summary>
/// <remarks>
/// Scoped services (<see cref="ICatalogStockService"/>, <see cref="IWalmartMarketplaceService"/>)
/// are resolved per-request via the route handler — NOT via the endpoint constructor, which would
/// capture a single scoped instance for the lifetime of this (effectively singleton) endpoint.
/// </remarks>
public class SyncWalmartStockEndpoint : IEndpoint<IResult, EmptyRequest, IWalmartMarketplaceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/walmart/stock/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IWalmartMarketplaceService walmart, ICatalogStockService catalogStock, CancellationToken ct) =>
            {
                return await HandleAsync(walmart, catalogStock, ct);
            })
            .Produces<SyncWalmartStockResponse>()
            .WithTags("WalmartEndpoints");
    }

    // Required by IEndpoint; the live route is served by the AddRoute handler above (which resolves
    // scoped services per-request). Never invoked by the framework.
    public Task<IResult> HandleAsync(EmptyRequest request, IWalmartMarketplaceService walmart)
        => throw new System.NotSupportedException("Routed via AddRoute with per-request scoped services.");

    public async Task<IResult> HandleAsync(
        IWalmartMarketplaceService walmart, ICatalogStockService catalogStock, CancellationToken ct)
    {
        try
        {
            var levels = await catalogStock.GetAllStockAsync(ct);
            var desired = levels.Select(l => new WalmartSkuStock(l.Sku, l.Quantity)).ToList();

            var results = await walmart.SyncAsync(desired, ct);

            return Results.Ok(new SyncWalmartStockResponse(
                results.Select(r => new WalmartSyncResultDto(r.Sku, r.Status)).ToList()));
        }
        catch (WalmartIntegrationException ex)
        {
            return WalmartEndpointResults.Error(ex);
        }
    }
}
