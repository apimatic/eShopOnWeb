using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WalmartEndpoints;

/// <summary>
/// Reads a SKU's stock as Walmart currently records it — the live value from Walmart, not a copy
/// this application kept from when it was set. Operator action.
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
            .Produces<WalmartStockResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("WalmartEndpoints");
    }

    public async Task<IResult> HandleAsync(string sku, IWalmartInventoryService walmart)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return Results.BadRequest("A SKU is required.");
        }

        var result = await walmart.GetStockAsync(sku);
        return Results.Ok(new WalmartStockResponse
        {
            Sku = result.Sku,
            Quantity = result.Quantity
        });
    }
}
