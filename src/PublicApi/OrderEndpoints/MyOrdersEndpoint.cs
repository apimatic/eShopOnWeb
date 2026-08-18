using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Shared;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Lists the caller's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IReadRepository<Order> orderRepository,
             IReadRepository<OrderNotification> notificationRepository, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(user, orderRepository, notificationRepository, notificationService);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        ClaimsPrincipal user,
        IReadRepository<Order> orderRepository,
        IReadRepository<OrderNotification> notificationRepository,
        IOrderNotificationService notificationService)
    {
        var buyerId = user.UserId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));

        var summaries = new List<OrderSummaryDto>();
        foreach (var order in orders)
        {
            var notifications = await notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(order.Id));
            // Refresh from the provider so each order shows where its notifications actually got to.
            await notificationService.RefreshStatusesAsync(notifications);

            summaries.Add(new OrderSummaryDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Notifications = notifications.Select(NotificationDto.From).ToList()
            });
        }

        var response = new MyOrdersResponse { Orders = summaries };
        return Results.Ok(response);
    }
}
