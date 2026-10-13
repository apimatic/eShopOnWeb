using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.CatalogItemEndpoints;

/// <summary>Body for setting eShop's own stock level for a catalog item (id comes from the route).</summary>
public class UpdateCatalogItemStockRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class UpdateCatalogItemStockResponse
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>
/// Sets eShop's own stock level for a catalog item. Operator action (administrators only). Does not
/// call Walmart — Walmart learns the new level at the next sync.
/// </summary>
public class UpdateCatalogItemStockEndpoint : IEndpoint<IResult, UpdateCatalogItemStockRequest, IRepository<CatalogItem>, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/catalog-items/{catalogItemId}/stock",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int catalogItemId, UpdateCatalogItemStockRequest request, IRepository<CatalogItem> itemRepository, CancellationToken ct) =>
            {
                request.CatalogItemId = catalogItemId;
                return await HandleAsync(request, itemRepository, ct);
            })
            .Produces<UpdateCatalogItemStockResponse>()
            .WithTags("CatalogItemEndpoints");
    }

    public async Task<IResult> HandleAsync(UpdateCatalogItemStockRequest request, IRepository<CatalogItem> itemRepository, CancellationToken ct)
    {
        if (request.Quantity < 0)
        {
            return Results.BadRequest("Quantity must be zero or greater.");
        }

        var item = await itemRepository.GetByIdAsync(request.CatalogItemId, ct);
        if (item is null)
        {
            return Results.NotFound();
        }

        item.SetStock(request.Quantity);
        await itemRepository.UpdateAsync(item, ct);

        return Results.Ok(new UpdateCatalogItemStockResponse
        {
            CatalogItemId = item.Id,
            Quantity = item.StockQuantity
        });
    }
}
