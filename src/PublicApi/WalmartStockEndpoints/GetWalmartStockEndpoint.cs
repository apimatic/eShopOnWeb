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
/// Reads a single SKU's stock as Walmart currently records it (read back from Walmart, not a local
/// copy). Operator action (administrators only).
/// </summary>
public class GetWalmartStockEndpoint : IEndpoint<IResult, string, IWalmartInventoryService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/walmart/stock/{sku}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string sku, IWalmartInventoryService service, CancellationToken ct) =>
            {
                return await HandleAsync(sku, service, ct);
            })
            .Produces<WalmartStockResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    public async Task<IResult> HandleAsync(string sku, IWalmartInventoryService service, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return Results.BadRequest("A SKU is required.");
        }

        try
        {
            var stock = await service.GetStockAsync(sku, ct);
            return Results.Ok(new WalmartStockResponse { Sku = stock.Sku, Quantity = stock.Quantity });
        }
        catch (WalmartInventoryException ex)
        {
            return WalmartResults.ToResult(ex);
        }
    }
}
