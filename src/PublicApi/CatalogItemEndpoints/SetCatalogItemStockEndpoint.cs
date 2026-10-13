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

namespace Microsoft.eShopWeb.PublicApi.CatalogItemEndpoints;

public class SetCatalogItemStockRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class SetCatalogItemStockResponse
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
    public string Sku { get; set; } = string.Empty;
}

/// <summary>
/// PUT /api/catalog-items/{catalogItemId}/stock — operator action: sets eShop's own stock level for a
/// catalog item. Does not call Walmart (Walmart learns the level at the next sync). Admin only.
/// </summary>
public class SetCatalogItemStockEndpoint : IEndpoint<IResult, SetCatalogItemStockRequest, IRepository<CatalogItem>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/catalog-items/{catalogItemId}/stock",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int catalogItemId, SetCatalogItemStockRequest request, IRepository<CatalogItem> itemRepository, CancellationToken ct) =>
            {
                request ??= new SetCatalogItemStockRequest();
                request.CatalogItemId = catalogItemId;
                return await HandleAsync(request, itemRepository, ct);
            })
            .Produces<SetCatalogItemStockResponse>()
            .WithTags("CatalogItemEndpoints");
    }

    public Task<IResult> HandleAsync(SetCatalogItemStockRequest request, IRepository<CatalogItem> itemRepository)
        => HandleAsync(request, itemRepository, default);

    public async Task<IResult> HandleAsync(SetCatalogItemStockRequest request, IRepository<CatalogItem> itemRepository, CancellationToken ct)
    {
        if (request.Quantity < 0)
        {
            return Results.BadRequest(new { message = "quantity must be zero or greater." });
        }

        var item = await itemRepository.GetByIdAsync(request.CatalogItemId, ct);
        if (item is null)
        {
            return Results.NotFound();
        }

        item.SetStock(request.Quantity);
        await itemRepository.UpdateAsync(item, ct);

        return Results.Ok(new SetCatalogItemStockResponse
        {
            CatalogItemId = item.Id,
            Quantity = item.UnitsInStock,
            Sku = WalmartSku.ForCatalogItem(item.Id),
        });
    }
}
