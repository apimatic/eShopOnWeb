using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IRepository<Order>>
{
    private readonly IOrderNotificationService _notifications;

    public ListOrderNotificationsEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orders, HttpContext httpContext) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest(orderId, httpContext.User.GetBuyerId()), orders);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IRepository<Order> orders)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var order = await orders.GetByIdAsync(request.OrderId);
        if (order == null || order.BuyerId != request.BuyerId)
        {
            return Results.NotFound();
        }

        var notifications = await _notifications.ListForOrderAsync(order.Id, refreshFromProvider: true);
        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Notifications = notifications.Select(NotificationDto.From).ToList()
        });
    }
}

public record ListOrderNotificationsRequest(int OrderId, string? BuyerId);
