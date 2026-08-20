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

public class GetMyOrdersEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    private readonly IReadRepository<OrderNotification> _notifications;

    public GetMyOrdersEndpoint(IReadRepository<OrderNotification> notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderNotificationService notificationService, HttpContext httpContext) =>
            {
                return await HandleAsync(notificationService, httpContext);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService notificationService)
        => HandleAsync(notificationService, null!);

    private async Task<IResult> HandleAsync(IOrderNotificationService notificationService, HttpContext httpContext)
    {
        var buyerId = httpContext.GetRequiredBuyerId();
        var orders = await notificationService.GetBuyerOrdersAsync(buyerId);
        var notifications = await _notifications.ListAsync(new OrderNotificationsByBuyerIdSpec(buyerId));
        var byOrder = notifications.GroupBy(notification => notification.OrderId)
            .ToDictionary(group => group.Key, group => group.Select(NotificationDto.From).ToList());

        var response = new ListMyOrdersResponse();
        response.Orders.AddRange(orders.Select(order => new OrderSummaryDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Notifications = byOrder.TryGetValue(order.Id, out var items) ? items : new List<NotificationDto>()
        }));

        return Results.Ok(response);
    }
}
