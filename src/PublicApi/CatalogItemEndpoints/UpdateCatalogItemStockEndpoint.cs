using System.ComponentModel.DataAnnotations;
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

/// <summary>
/// Operator endpoint: PUT /api/catalog-items/{catalogItemId}/stock
/// Sets eShop's own stock level for a catalog item. Does not call Walmart; Walmart learns the new
/// level at the next sync.
/// </summary>
public class UpdateCatalogItemStockEndpoint : IEndpoint<IResult, UpdateCatalogItemStockRequest, IRepository<CatalogItem>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/catalog-items/{catalogItemId}/stock",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int catalogItemId, UpdateCatalogItemStockRequest request, IRepository<CatalogItem> itemRepository, CancellationToken ct) =>
            {
                request.CatalogItemId = catalogItemId;
                return await HandleAsync(request, itemRepository);
            })
            .Produces<UpdateCatalogItemStockResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("CatalogItemEndpoints");
    }

    public async Task<IResult> HandleAsync(UpdateCatalogItemStockRequest request, IRepository<CatalogItem> itemRepository)
    {
        if (request.Quantity < 0)
        {
            return Results.BadRequest(new { message = "quantity must be zero or greater." });
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

public class UpdateCatalogItemStockRequest
{
    public int CatalogItemId { get; set; }

    [Required]
    public int Quantity { get; set; }
}

public class UpdateCatalogItemStockResponse
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}
