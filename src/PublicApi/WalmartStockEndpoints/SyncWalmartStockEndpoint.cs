using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Walmart;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

public class SyncResultDto
{
    public string Sku { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class SyncWalmartStockResponse : BaseResponse
{
    public SyncWalmartStockResponse(Guid correlationId) : base(correlationId) { }
    public SyncWalmartStockResponse() { }

    public List<SyncResultDto> Results { get; set; } = new();
}

/// <summary>
/// Flow 2 (operator): pushes the stock level of every catalog item to Walmart in one request.
/// The levels are the ones eShop holds (an item whose level was never set has a level of 0). The
/// response reports, per SKU, what Walmart did with it.
/// </summary>
public class SyncWalmartStockEndpoint : IEndpoint<IResult, IRepository<CatalogItem>>
{
    private readonly IWalmartInventoryService _walmart;

    public SyncWalmartStockEndpoint(IWalmartInventoryService walmart) => _walmart = walmart;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/walmart/stock/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IRepository<CatalogItem> itemRepository, CancellationToken cancellationToken) =>
                await HandleAsync(itemRepository, cancellationToken))
            .Produces<SyncWalmartStockResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    // Interface member — the framework only calls AddRoute; the route lambda calls the ct overload.
    public Task<IResult> HandleAsync(IRepository<CatalogItem> itemRepository) =>
        HandleAsync(itemRepository, CancellationToken.None);

    public async Task<IResult> HandleAsync(IRepository<CatalogItem> itemRepository, CancellationToken cancellationToken)
    {
        var catalogItems = await itemRepository.ListAsync(cancellationToken);
        var payload = catalogItems
            .Select(ci => new WalmartSkuQuantity(WalmartSku.ForCatalogItem(ci.Id), ci.AvailableStock))
            .ToList();

        try
        {
            var results = await _walmart.SyncAsync(payload, cancellationToken);
            return Results.Ok(new SyncWalmartStockResponse
            {
                Results = results.Select(r => new SyncResultDto { Sku = r.Sku, Status = r.Status }).ToList(),
            });
        }
        catch (WalmartIntegrationException ex)
        {
            return WalmartResults.Problem(ex);
        }
    }
}
