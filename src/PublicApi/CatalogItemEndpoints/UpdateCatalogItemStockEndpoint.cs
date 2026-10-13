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

/// <summary>
/// Operator action. Sets eShop's own stock level for a catalog item. Does not call Walmart; Walmart
/// learns the new level at the next sync.
/// </summary>
public class UpdateCatalogItemStockEndpoint : IEndpoint<IResult, UpdateCatalogItemStockRequest, IRepository<CatalogItem>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/catalog-items/{catalogItemId}/stock",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int catalogItemId, UpdateCatalogItemStockRequest request, IRepository<CatalogItem> itemRepository) =>
            {
                request ??= new UpdateCatalogItemStockRequest();
                request.CatalogItemId = catalogItemId;
                return await HandleAsync(request, itemRepository);
            })
            .Produces<UpdateCatalogItemStockResponse>()
            .WithTags("CatalogItemEndpoints");
    }

    public async Task<IResult> HandleAsync(UpdateCatalogItemStockRequest request, IRepository<CatalogItem> itemRepository)
    {
        if (request.Quantity < 0)
        {
            return Results.BadRequest(new { message = "quantity must be a non-negative integer." });
        }

        var item = await itemRepository.GetByIdAsync(request.CatalogItemId);
        if (item is null)
        {
            return Results.NotFound();
        }

        item.SetStock(request.Quantity);
        await itemRepository.UpdateAsync(item);

        return Results.Ok(new UpdateCatalogItemStockResponse
        {
            CatalogItemId = item.Id,
            Quantity = item.StockQuantity
        });
    }
}

/// <summary>Body of PUT /api/catalog-items/{catalogItemId}/stock. The id comes from the route.</summary>
public class UpdateCatalogItemStockRequest
{
    public int Quantity { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int CatalogItemId { get; set; }
}

public class UpdateCatalogItemStockResponse
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}
