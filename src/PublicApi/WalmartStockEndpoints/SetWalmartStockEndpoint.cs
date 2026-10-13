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

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

/// <summary>
/// Sets how many of a SKU the shop offers on Walmart. Operator action (administrators only).
/// Afterwards Walmart's own record of the SKU is that number; eShop's own level is unchanged.
/// </summary>
public class SetWalmartStockEndpoint : IEndpoint<IResult, SetWalmartStockRequest, IWalmartInventoryService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/walmart/stock/{sku}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string sku, SetWalmartStockRequest request, IWalmartInventoryService service, CancellationToken ct) =>
            {
                request.Sku = sku;
                return await HandleAsync(request, service, ct);
            })
            .Produces<WalmartStockResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    public async Task<IResult> HandleAsync(SetWalmartStockRequest request, IWalmartInventoryService service, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            return Results.BadRequest("A SKU is required.");
        }
        if (request.Quantity < 0)
        {
            return Results.BadRequest("Quantity must be zero or greater.");
        }

        try
        {
            var stock = await service.SetStockAsync(request.Sku, request.Quantity, ct);
            return Results.Ok(new WalmartStockResponse { Sku = stock.Sku, Quantity = stock.Quantity });
        }
        catch (WalmartInventoryException ex)
        {
            return WalmartResults.ToResult(ex);
        }
    }
}
