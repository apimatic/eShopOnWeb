using System.Collections.Generic;
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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationsFeature;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the signed-in shopper's own orders, each showing where its notifications got to.
/// Notification delivery outcomes are refreshed against the provider.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IRepository<Order> orderRepository, IOrderNotificationService notificationService) =>
                await HandleAsync(user, orderRepository, notificationService))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public static async Task<IResult> HandleAsync(
        ClaimsPrincipal user,
        IRepository<Order> orderRepository,
        IOrderNotificationService notificationService)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await orderRepository.ListAsync(new CustomerOrdersSpecification(buyerId));

        var items = new List<MyOrderDto>();
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            var notifications = await notificationService.GetNotificationsForOrderAsync(order.Id);
            items.Add(new MyOrderDto(
                order.Id,
                order.Status.ToString(),
                order.Total(),
                order.OrderDate,
                notifications.Select(NotificationDto.From).ToList()));
        }

        return Results.Ok(new MyOrdersResponse(items));
    }
}

public record MyOrderDto(
    int OrderId,
    string Status,
    decimal Total,
    System.DateTimeOffset OrderDate,
    List<NotificationDto> Notifications);

public record MyOrdersResponse(List<MyOrderDto> Orders);
