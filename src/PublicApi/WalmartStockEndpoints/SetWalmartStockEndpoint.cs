using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.WalmartIntegration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

/// <summary>
/// PUT /api/walmart/stock/{sku} — operator action: sets how many of the item the shop offers on
/// Walmart. Does not change eShop's own level. Admin only.
/// </summary>
public class SetWalmartStockEndpoint : IEndpoint<IResult, SetWalmartStockRequest, IWalmartInventoryService, IReadRepository<CatalogItem>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/walmart/stock/{sku}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string sku, SetWalmartStockRequest request, IWalmartInventoryService walmart, IReadRepository<CatalogItem> catalog, CancellationToken ct) =>
            {
                request ??= new SetWalmartStockRequest();
                request.Sku = sku;
                return await HandleAsync(request, walmart, catalog, ct);
            })
            .Produces<WalmartStockResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    public Task<IResult> HandleAsync(SetWalmartStockRequest request, IWalmartInventoryService walmart, IReadRepository<CatalogItem> catalog)
        => HandleAsync(request, walmart, catalog, default);

    public async Task<IResult> HandleAsync(SetWalmartStockRequest request, IWalmartInventoryService walmart, IReadRepository<CatalogItem> catalog, CancellationToken ct)
    {
        if (request.Quantity < 0)
        {
            return Results.BadRequest(new { message = "quantity must be zero or greater." });
        }

        // The SKU must be one eShop offers: ESHOP-{id} for an existing catalog item.
        if (!WalmartSku.TryGetCatalogItemId(request.Sku, out var catalogItemId))
        {
            return Results.NotFound(new { message = $"'{request.Sku}' is not an eShop seller SKU." });
        }
        var item = await catalog.GetByIdAsync(catalogItemId, ct);
        if (item is null)
        {
            return Results.NotFound(new { message = $"No catalog item maps to SKU '{request.Sku}'." });
        }

        await walmart.SetStockAsync(request.Sku, request.Quantity, ct);

        return Results.Ok(new WalmartStockResponse { Sku = request.Sku, Quantity = request.Quantity });
    }
}
