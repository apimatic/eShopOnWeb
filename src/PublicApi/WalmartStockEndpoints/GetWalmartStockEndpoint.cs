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
/// Returns the SKU's stock as Walmart currently records it — read live from Walmart, not a copy this
/// application kept from when it was set. Operator-only.
/// </summary>
public class GetWalmartStockEndpoint : IEndpoint<IResult, GetWalmartStockRequest, IWalmartInventoryService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/walmart/stock/{sku}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string sku, IWalmartInventoryService walmart) =>
            {
                return await HandleAsync(new GetWalmartStockRequest(sku), walmart);
            })
            .Produces<GetWalmartStockResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("WalmartStockEndpoints");
    }

    public async Task<IResult> HandleAsync(GetWalmartStockRequest request, IWalmartInventoryService walmart)
    {
        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            return Results.BadRequest("A SKU is required.");
        }

        var snapshot = await walmart.GetInventoryAsync(request.Sku);
        if (snapshot is null)
        {
            return Results.NotFound();
        }

        var response = new GetWalmartStockResponse(request.CorrelationId())
        {
            Sku = snapshot.Sku,
            Quantity = snapshot.Quantity
        };
        return Results.Ok(response);
    }
}
