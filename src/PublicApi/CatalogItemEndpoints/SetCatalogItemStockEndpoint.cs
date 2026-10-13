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

/// <summary>Body of <c>PUT /api/catalog-items/{catalogItemId}/stock</c>.</summary>
public record SetCatalogItemStockRequest
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public record SetCatalogItemStockResponse(int CatalogItemId, int AvailableStock);

/// <summary>
/// PUT /api/catalog-items/{catalogItemId}/stock — sets eShop's OWN stock level for a catalog item. Operator
/// action (administrator role). Does not call Walmart; Walmart learns the new level at the next sync.
/// </summary>
public class SetCatalogItemStockEndpoint : IEndpoint<IResult, SetCatalogItemStockRequest, IRepository<CatalogItem>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/catalog-items/{catalogItemId}/stock",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int catalogItemId, SetCatalogItemStockRequest request, IRepository<CatalogItem> itemRepository) =>
            {
                return await HandleAsync(request with { CatalogItemId = catalogItemId }, itemRepository);
            })
            .Produces<SetCatalogItemStockResponse>()
            .WithTags("CatalogItemEndpoints");
    }

    public async Task<IResult> HandleAsync(SetCatalogItemStockRequest request, IRepository<CatalogItem> itemRepository)
    {
        if (request.Quantity < 0)
        {
            return Results.BadRequest("quantity must be zero or greater.");
        }

        var item = await itemRepository.GetByIdAsync(request.CatalogItemId);
        if (item is null)
        {
            return Results.NotFound();
        }

        item.SetStockLevel(request.Quantity);
        await itemRepository.UpdateAsync(item);

        return Results.Ok(new SetCatalogItemStockResponse(item.Id, item.AvailableStock));
    }
}
