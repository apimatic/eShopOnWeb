using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Walmart;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WalmartEndpoints;

/// <summary>
/// Operator endpoint: GET /api/walmart/stock/{sku}
/// Returns the SKU's stock as Walmart currently records it — read from Walmart, not a copy this
/// application kept. 404 if Walmart holds no record for the SKU.
/// </summary>
public class GetWalmartStockEndpoint : IEndpoint<IResult, string, IWalmartStockService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/walmart/stock/{sku}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string sku, IWalmartStockService stockService, CancellationToken ct) =>
            {
                return await HandleAsync(sku, stockService);
            })
            .Produces<GetWalmartStockResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("WalmartStockEndpoints");
    }

    public async Task<IResult> HandleAsync(string sku, IWalmartStockService stockService)
    {
        var record = await stockService.GetWalmartQuantityAsync(sku);
        if (record is null)
        {
            return Results.NotFound(new { sku, message = "Walmart holds no inventory record for this SKU." });
        }

        return Results.Ok(new GetWalmartStockResponse
        {
            Sku = record.Value.Sku,
            Quantity = record.Value.Quantity
        });
    }
}

public class GetWalmartStockResponse
{
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
}
