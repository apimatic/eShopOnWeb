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

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, ICatalogOrderService>
{
    private readonly IOrderNotificationService _notifications;

    public PlaceOrderEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, HttpContext http, ICatalogOrderService orders) =>
            {
                return await HandleAsync(request, http, orders);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, ICatalogOrderService orders)
        => HandleAsync(request, null!, orders);

    private async Task<IResult> HandleAsync(PlaceOrderRequest request, HttpContext http, ICatalogOrderService orders)
    {
        var buyerId = http.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var items = request.Items
            .Select(i => new CatalogOrderItemRequest(i.CatalogItemId, i.Quantity))
            .ToList();
        var order = await orders.PlaceOrderAsync(buyerId, items, http.RequestAborted);
        await _notifications.NotifyOrderPlacedAsync(order, http.RequestAborted);
        var created = await _notifications.ListForOrderAsync(order.Id, http.RequestAborted);

        var response = new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Notifications = NotificationDtoMapper.ToDtos(created)
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
