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
/// Sets how many of a single SKU the shop offers on Walmart. Afterwards Walmart's own record of the
/// SKU's quantity is that number. Does not change eShop's own level for the item. Operator-only.
/// </summary>
public class SetWalmartStockEndpoint : IEndpoint<IResult, SetWalmartStockRequest, IWalmartInventoryService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/walmart/stock/{sku}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string sku, SetWalmartStockRequest request, IWalmartInventoryService walmart) =>
            {
                return await HandleAsync(new SetWalmartStockRequest(sku, request.Quantity), walmart);
            })
            .Produces<SetWalmartStockResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("WalmartStockEndpoints");
    }

    public async Task<IResult> HandleAsync(SetWalmartStockRequest request, IWalmartInventoryService walmart)
    {
        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            return Results.BadRequest("A SKU is required.");
        }

        if (request.Quantity < 0)
        {
            return Results.BadRequest("Quantity must be zero or greater.");
        }

        var result = await walmart.SetInventoryAsync(request.Sku, request.Quantity);

        var response = new SetWalmartStockResponse(request.CorrelationId())
        {
            Sku = result.Sku,
            Quantity = result.Quantity
        };
        return Results.Ok(response);
    }
}
