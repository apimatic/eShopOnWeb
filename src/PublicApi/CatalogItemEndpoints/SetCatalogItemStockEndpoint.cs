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
/// Sets eShop's own stock level for a catalog item. This is an operator action and does not call
/// Walmart — Walmart learns the new level at the next sync.
/// </summary>
public class SetCatalogItemStockEndpoint : IEndpoint<IResult, SetCatalogItemStockRequest, int, IRepository<CatalogItem>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/catalog-items/{catalogItemId}/stock",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int catalogItemId, SetCatalogItemStockRequest request, IRepository<CatalogItem> itemRepository) =>
            {
                return await HandleAsync(request, catalogItemId, itemRepository);
            })
            .Produces<SetCatalogItemStockResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("CatalogItemEndpoints");
    }

    public async Task<IResult> HandleAsync(SetCatalogItemStockRequest request, int catalogItemId, IRepository<CatalogItem> itemRepository)
    {
        var response = new SetCatalogItemStockResponse(request.CorrelationId());

        var item = await itemRepository.GetByIdAsync(catalogItemId);
        if (item is null)
        {
            return Results.NotFound();
        }

        if (request.Quantity < 0)
        {
            return Results.BadRequest("Quantity must be zero or greater.");
        }

        item.SetStock(request.Quantity);
        await itemRepository.UpdateAsync(item);

        response.CatalogItemId = item.Id;
        response.Quantity = item.StockQuantity;
        return Results.Ok(response);
    }
}
