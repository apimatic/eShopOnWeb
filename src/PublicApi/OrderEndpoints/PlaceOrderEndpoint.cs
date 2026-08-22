using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderService>
{
    private readonly IOrderNotificationService _notificationService;

    public PlaceOrderEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderService orderService, ClaimsPrincipal user) =>
            {
                var unauthorized = HttpCaller.RequireBuyerId(user, out var buyerId);
                if (unauthorized is not null)
                {
                    return unauthorized;
                }

                return await HandleAsync(request, orderService, buyerId);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderService orderService)
        => HandleAsync(request, orderService, string.Empty);

    private async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderService orderService, string buyerId)
    {
        var lines = request.Items
            .Select(item => new CatalogOrderLine(item.CatalogItemId, item.Quantity))
            .ToList();

        var address = new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        var order = await orderService.CreateOrderFromCatalogItemsAsync(buyerId, lines, address);
        await _notificationService.NotifyOrderPlacedAsync(order, default);

        var response = new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
