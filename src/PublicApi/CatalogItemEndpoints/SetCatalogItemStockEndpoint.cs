using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.CatalogItemEndpoints;

/// <summary>Body of PUT /api/catalog-items/{catalogItemId}/stock.</summary>
public class SetCatalogItemStockRequest
{
    [Range(0, int.MaxValue)]
    public int Quantity { get; set; }
}

/// <summary>Response: the catalog item id and its new eShop level (both top-level).</summary>
public record SetCatalogItemStockResponse(int CatalogItemId, int Quantity);

/// <summary>Bundled inputs for PUT /api/catalog-items/{catalogItemId}/stock.</summary>
public record SetCatalogItemStockCommand(int CatalogItemId, int Quantity);

/// <summary>
/// Sets eShop's own stock level for a catalog item. Does not call Walmart — Walmart learns the new
/// level at the next sync. Operator action.
/// </summary>
public class SetCatalogItemStockEndpoint : IEndpoint<IResult, SetCatalogItemStockCommand, ICatalogStockService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/catalog-items/{catalogItemId}/stock",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int catalogItemId, SetCatalogItemStockRequest request, ICatalogStockService stock, CancellationToken ct) =>
            {
                return await HandleAsync(new SetCatalogItemStockCommand(catalogItemId, request.Quantity), stock, ct);
            })
            .Produces<SetCatalogItemStockResponse>()
            .WithTags("CatalogItemEndpoints");
    }

    public Task<IResult> HandleAsync(SetCatalogItemStockCommand request, ICatalogStockService stock)
        => HandleAsync(request, stock, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        SetCatalogItemStockCommand request, ICatalogStockService stock, CancellationToken ct)
    {
        try
        {
            await stock.SetStockAsync(request.CatalogItemId, request.Quantity, ct);
            return Results.Ok(new SetCatalogItemStockResponse(request.CatalogItemId, request.Quantity));
        }
        catch (InsufficientStockException ex)
        {
            // SetStockAsync raises this only when the catalog item does not exist.
            return Results.NotFound(new { message = ex.Message });
        }
    }
}
