using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, HttpContext httpContext, IShopperOrderService service) =>
            {
                return await HandleAsync(request, httpContext, service);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IShopperOrderService service)
        => HandleAsync(request, null!, service);

    private async Task<IResult> HandleAsync(PlaceOrderRequest request, HttpContext httpContext, IShopperOrderService service)
    {
        var buyerId = httpContext.BuyerId();
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var lines = (request.Items ?? []).Select(i => new CatalogOrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var order = await service.PlaceAsync(buyerId, lines, null, httpContext.RequestAborted);
        var response = new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
