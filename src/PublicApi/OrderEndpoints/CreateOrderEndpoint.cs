using System;
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

public class CreateOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IShopperOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IShopperOrderService orderService) =>
            {
                return await HandleAsync(request, orderService);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IShopperOrderService orderService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.GetBuyerId();
        if (string.IsNullOrWhiteSpace(buyerId))
            return Results.Unauthorized();

        try
        {
            var lines = (request.Items ?? new()).Select(i => new CatalogOrderLine(i.CatalogItemId, i.Quantity)).ToList();
            var order = await orderService.PlaceAsync(buyerId, lines, _httpContextAccessor.HttpContext!.RequestAborted);
            var response = new PlaceOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                FulfillmentStatus = order.FulfillmentStatus,
                Total = order.Total()
            };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (Exception ex)
        {
            return ex.ToHttpResult();
        }
    }
}
