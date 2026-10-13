using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

/// <summary>
/// Operator action. Returns a SKU's stock as Walmart currently records it — read live from
/// Walmart, not from any copy this application kept.
/// </summary>
public class GetWalmartStockEndpoint : IEndpoint<IResult, string, IWalmartInventoryService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/walmart/stock/{sku}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string sku, IWalmartInventoryService walmart) =>
            {
                return await HandleAsync(sku, walmart);
            })
            .Produces<WalmartSkuStockResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    public async Task<IResult> HandleAsync(string sku, IWalmartInventoryService walmart)
    {
        var record = await walmart.GetStockAsync(sku);
        if (record is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new WalmartSkuStockResponse { Sku = record.Sku, Quantity = record.Quantity });
    }
}
