using System.Threading;
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
/// Flow 1 (operator): the SKU's stock as Walmart currently records it — read live from Walmart,
/// not from a local copy.
/// </summary>
public class GetWalmartStockEndpoint : IEndpoint<IResult, string>
{
    private readonly IWalmartInventoryService _walmart;

    public GetWalmartStockEndpoint(IWalmartInventoryService walmart) => _walmart = walmart;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/walmart/stock/{sku}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string sku, CancellationToken cancellationToken) => await HandleAsync(sku, cancellationToken))
            .Produces<WalmartStockResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    // Interface member — the framework only calls AddRoute; the route lambda calls the ct overload.
    public Task<IResult> HandleAsync(string sku) => HandleAsync(sku, CancellationToken.None);

    public async Task<IResult> HandleAsync(string sku, CancellationToken cancellationToken)
    {
        try
        {
            var level = await _walmart.GetStockAsync(sku, cancellationToken);
            return Results.Ok(new WalmartStockResponse
            {
                Sku = level.Sku,
                Quantity = level.Quantity,
            });
        }
        catch (WalmartIntegrationException ex)
        {
            return WalmartResults.Problem(ex);
        }
    }
}
