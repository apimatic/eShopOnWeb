using System.Collections.Generic;
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

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, GetOrderNotificationsRequest, IRepository<Order>>
{
    private readonly IOrderNotificationService _notifications;

    public GetOrderNotificationsEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext httpContext, IRepository<Order> orders) =>
            {
                return await HandleAsync(new GetOrderNotificationsRequest(orderId, httpContext.User.GetRequiredBuyerId(), httpContext.User.IsAdministrator()), orders);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IRepository<Order> orders)
    {
        var order = await orders.FirstOrDefaultAsync(new OrderByIdSpec(request.OrderId));
        if (order is null)
        {
            return Results.NotFound();
        }

        if (!request.IsAdministrator && !string.Equals(order.BuyerId, request.BuyerId, System.StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        var notifications = await _notifications.GetForOrderAsync(order.Id, refreshFromProvider: true);
        return Results.Ok(new OrderNotificationsResponse
        {
            OrderId = order.Id,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        });
    }
}

public record GetOrderNotificationsRequest(int OrderId, string BuyerId, bool IsAdministrator);

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
