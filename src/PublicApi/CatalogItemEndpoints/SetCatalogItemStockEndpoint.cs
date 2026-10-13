using System;
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

public class SetCatalogItemStockRequest : BaseRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class SetCatalogItemStockResponse : BaseResponse
{
    public SetCatalogItemStockResponse(Guid correlationId) : base(correlationId) { }
    public SetCatalogItemStockResponse() { }

    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>
/// Operator: sets eShop's own stock level for a catalog item. Does not call Walmart — Walmart
/// learns the new level at the next sync.
/// </summary>
public class SetCatalogItemStockEndpoint
    : IEndpoint<IResult, SetCatalogItemStockRequest, IRepository<CatalogItem>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/catalog-items/{catalogItemId}/stock",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int catalogItemId, SetCatalogItemStockRequest request, IRepository<CatalogItem> itemRepository, CancellationToken cancellationToken) =>
            {
                request.CatalogItemId = catalogItemId;
                return await HandleAsync(request, itemRepository, cancellationToken);
            })
            .Produces<SetCatalogItemStockResponse>()
            .WithTags("CatalogItemEndpoints");
    }

    // Interface member — the framework only calls AddRoute; the route lambda calls the ct overload.
    public Task<IResult> HandleAsync(SetCatalogItemStockRequest request, IRepository<CatalogItem> itemRepository) =>
        HandleAsync(request, itemRepository, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        SetCatalogItemStockRequest request, IRepository<CatalogItem> itemRepository, CancellationToken cancellationToken)
    {
        if (request.Quantity < 0)
        {
            return Results.BadRequest("quantity must be zero or greater.");
        }

        var item = await itemRepository.GetByIdAsync(request.CatalogItemId, cancellationToken);
        if (item is null)
        {
            return Results.NotFound();
        }

        item.SetStock(request.Quantity);
        await itemRepository.UpdateAsync(item, cancellationToken);

        return Results.Ok(new SetCatalogItemStockResponse(request.CorrelationId())
        {
            CatalogItemId = item.Id,
            Quantity = item.AvailableStock,
        });
    }
}
