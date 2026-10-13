using System;
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

public class SetWalmartStockRequest : BaseRequest
{
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public class WalmartStockResponse : BaseResponse
{
    public WalmartStockResponse(Guid correlationId) : base(correlationId) { }
    public WalmartStockResponse() { }

    public string Sku { get; set; } = string.Empty;
    public int? Quantity { get; set; }
}

/// <summary>
/// Flow 1 (operator): sets how many of a SKU the shop offers on Walmart. Afterwards Walmart's own
/// record of the SKU's quantity is that number. Does not change eShop's own level.
/// </summary>
public class SetWalmartStockEndpoint : IEndpoint<IResult, SetWalmartStockRequest>
{
    private readonly IWalmartInventoryService _walmart;

    public SetWalmartStockEndpoint(IWalmartInventoryService walmart) => _walmart = walmart;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/walmart/stock/{sku}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string sku, SetWalmartStockRequest request, CancellationToken cancellationToken) =>
            {
                request.Sku = sku;
                return await HandleAsync(sku, request, cancellationToken);
            })
            .Produces<WalmartStockResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    // Interface member — the framework only calls AddRoute; the route lambda calls the ct overload.
    public Task<IResult> HandleAsync(SetWalmartStockRequest request) =>
        HandleAsync(request.Sku, request, CancellationToken.None);

    public async Task<IResult> HandleAsync(string sku, SetWalmartStockRequest request, CancellationToken cancellationToken)
    {
        if (request.Quantity < 0)
        {
            return Results.BadRequest("quantity must be zero or greater.");
        }

        try
        {
            var level = await _walmart.SetStockAsync(sku, request.Quantity, cancellationToken);
            return Results.Ok(new WalmartStockResponse(request.CorrelationId())
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
