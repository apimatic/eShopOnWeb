using System.Collections.Generic;
using System.Linq;
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

namespace Microsoft.eShopWeb.PublicApi.WalmartEndpoints;

/// <summary>
/// Lists every SKU Walmart holds stock for, lined up against eShop's own level for the matching
/// catalog item, so a SKU whose two numbers differ — or that exists on one side only — is visible.
/// Covers every SKU on both sides. Operator action.
/// </summary>
/// <remarks>
/// Scoped services are resolved per-request via the route handler — NOT via the endpoint
/// constructor, which would capture a single stale <c>CatalogContext</c> for the endpoint's life.
/// </remarks>
public class GetWalmartStockReportEndpoint : IEndpoint<IResult, EmptyRequest, IWalmartMarketplaceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/walmart/stock",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IWalmartMarketplaceService walmart, ICatalogStockService catalogStock, CancellationToken ct) =>
            {
                return await HandleAsync(walmart, catalogStock, ct);
            })
            .Produces<WalmartStockReportResponse>()
            .WithTags("WalmartEndpoints");
    }

    // Required by IEndpoint; the live route is served by the AddRoute handler above. Never invoked.
    public Task<IResult> HandleAsync(EmptyRequest request, IWalmartMarketplaceService walmart)
        => throw new System.NotSupportedException("Routed via AddRoute with per-request scoped services.");

    public async Task<IResult> HandleAsync(
        IWalmartMarketplaceService walmart, ICatalogStockService catalogStock, CancellationToken ct)
    {
        try
        {
            var walmartStock = await walmart.ListAllStockAsync(ct);
            var eshopLevels = await catalogStock.GetAllStockAsync(ct);

            // Walmart may report a SKU at more than one ship node — aggregate to one total per SKU.
            var walmartBySku = walmartStock
                .GroupBy(w => w.Sku)
                .ToDictionary(g => g.Key, g => g.Sum(w => w.Quantity));

            // Every catalog item appears with its eShop level (0 where never set).
            var eshopBySku = eshopLevels.ToDictionary(e => e.Sku, e => e.Quantity);

            var allSkus = new SortedSet<string>(walmartBySku.Keys, System.StringComparer.Ordinal);
            allSkus.UnionWith(eshopBySku.Keys);

            var items = allSkus
                .Select(sku => new WalmartStockComparisonDto(
                    sku,
                    walmartBySku.TryGetValue(sku, out var wq) ? wq : (int?)null,
                    eshopBySku.TryGetValue(sku, out var eq) ? eq : (int?)null))
                .ToList();

            return Results.Ok(new WalmartStockReportResponse(items));
        }
        catch (WalmartIntegrationException ex)
        {
            return WalmartEndpointResults.Error(ex);
        }
    }
}
