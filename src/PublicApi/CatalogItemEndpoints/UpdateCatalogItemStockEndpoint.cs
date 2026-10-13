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
/// Sets eShop's own stock level for a catalog item. This is eShop's level only; Walmart learns the
/// new level at the next sync. Operator-only.
/// </summary>
public class UpdateCatalogItemStockEndpoint : IEndpoint<IResult, UpdateCatalogItemStockRequest, IRepository<CatalogItem>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/catalog-items/{catalogItemId}/stock",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int catalogItemId, UpdateCatalogItemStockRequest request, IRepository<CatalogItem> itemRepository) =>
            {
                return await HandleAsync(new UpdateCatalogItemStockRequest(catalogItemId, request.Quantity), itemRepository);
            })
            .Produces<UpdateCatalogItemStockResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("CatalogItemEndpoints");
    }

    public async Task<IResult> HandleAsync(UpdateCatalogItemStockRequest request, IRepository<CatalogItem> itemRepository)
    {
        if (request.Quantity < 0)
        {
            return Results.BadRequest("Quantity must be zero or greater.");
        }

        var existingItem = await itemRepository.GetByIdAsync(request.CatalogItemId);
        if (existingItem is null)
        {
            return Results.NotFound();
        }

        existingItem.SetStock(request.Quantity);
        await itemRepository.UpdateAsync(existingItem);

        var response = new UpdateCatalogItemStockResponse(request.CorrelationId())
        {
            CatalogItemId = existingItem.Id,
            Quantity = existingItem.StockQuantity
        };
        return Results.Ok(response);
    }
}
