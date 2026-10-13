using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Walmart;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

/// <summary>
/// GET /api/walmart/stock — lists every SKU Walmart holds stock for, lined up against eShop's own level for
/// the matching catalog item, so a SKU whose two numbers differ, or that exists on one side only, is visible.
/// Operator action (administrator role).
/// </summary>
public class GetWalmartStockReportEndpoint : IEndpoint<IResult, GetWalmartStockReportRequest, IWalmartInventoryService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/walmart/stock",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IWalmartInventoryService inventoryService) =>
            {
                return await HandleAsync(new GetWalmartStockReportRequest(), inventoryService);
            })
            .Produces<WalmartStockReportResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    public async Task<IResult> HandleAsync(GetWalmartStockReportRequest request, IWalmartInventoryService inventoryService)
    {
        var report = await inventoryService.GetStockComparisonAsync(default);
        return Results.Ok(new WalmartStockReportResponse(report.Items, report.Partial));
    }
}
