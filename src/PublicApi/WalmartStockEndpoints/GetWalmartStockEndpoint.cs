using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.WalmartIntegration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

/// <summary>
/// GET /api/walmart/stock/{sku} — operator action: the SKU's stock as Walmart currently records it
/// (Walmart's own record, read live). Admin only.
/// </summary>
public class GetWalmartStockEndpoint : IEndpoint<IResult, string, IWalmartInventoryService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/walmart/stock/{sku}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string sku, IWalmartInventoryService walmart, CancellationToken ct) =>
            {
                return await HandleAsync(sku, walmart, ct);
            })
            .Produces<WalmartStockResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    public Task<IResult> HandleAsync(string sku, IWalmartInventoryService walmart)
        => HandleAsync(sku, walmart, default);

    public async Task<IResult> HandleAsync(string sku, IWalmartInventoryService walmart, CancellationToken ct)
    {
        if (!WalmartSku.TryGetCatalogItemId(sku, out _))
        {
            return Results.BadRequest(new { message = $"'{sku}' is not a well-formed eShop seller SKU." });
        }

        var quantity = await walmart.GetStockAsync(sku, ct);

        return Results.Ok(new WalmartStockResponse { Sku = sku, Quantity = quantity });
    }
}
