using System.Linq;
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

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, IRepository<Order>>
{
    private readonly IOrderNotificationService _notifications;

    public ListOrderNotificationsEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext http, IRepository<Order> orders) =>
            {
                return await HandleAsync(orderId, http, orders);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IRepository<Order> orders)
        => HandleAsync(orderId, null!, orders);

    private async Task<IResult> HandleAsync(int orderId, HttpContext http, IRepository<Order> orders)
    {
        var order = await orders.GetByIdAsync(orderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        var buyerId = http.GetRequiredBuyerId();
        if (!http.IsAdministrator() && !string.Equals(order.BuyerId, buyerId, System.StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        var notifications = await _notifications.GetForOrderAsync(orderId);
        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(OrderNotificationDto.From).ToList()
        });
    }
}
