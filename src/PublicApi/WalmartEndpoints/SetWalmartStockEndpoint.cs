using System.ComponentModel.DataAnnotations;
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
/// Operator endpoint: PUT /api/walmart/stock/{sku}
/// Sets how many of the item the shop offers on Walmart. Afterwards Walmart's own record of the
/// SKU's quantity is this number. Does not change eShop's own level for the item.
/// </summary>
public class SetWalmartStockEndpoint : IEndpoint<IResult, SetWalmartStockRequest, IWalmartStockService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/walmart/stock/{sku}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string sku, SetWalmartStockRequest request, IWalmartStockService stockService, CancellationToken ct) =>
            {
                request.Sku = sku;
                return await HandleAsync(request, stockService);
            })
            .Produces<SetWalmartStockResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("WalmartStockEndpoints");
    }

    public async Task<IResult> HandleAsync(SetWalmartStockRequest request, IWalmartStockService stockService)
    {
        if (request.Quantity < 0)
        {
            return Results.BadRequest(new { message = "quantity must be zero or greater." });
        }

        var result = await stockService.SetWalmartQuantityAsync(request.Sku, request.Quantity);
        return Results.Ok(new SetWalmartStockResponse
        {
            Sku = result.Sku,
            Quantity = result.Quantity
        });
    }
}

public class SetWalmartStockRequest
{
    /// <summary>Seller SKU, taken from the route.</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>The quantity to record on Walmart for this SKU.</summary>
    [Required]
    public int Quantity { get; set; }
}

public class SetWalmartStockResponse
{
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
}
