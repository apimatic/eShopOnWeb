using System;
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
using Microsoft.eShopWeb.PublicApi.SmsNotifications;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record MyOrderDto(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    int ItemCount,
    IReadOnlyList<NotificationView> Notifications);

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

/// <summary>
/// The caller's own orders, each showing where its notifications got to. Delivery outcomes are
/// refreshed from the provider on read, since there is no callback URL into this application.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, HttpContext http) => await HandleAsync(http))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        var ownerId = CallerIdentity.GetOwnerId(http.User);
        if (string.IsNullOrEmpty(ownerId))
            return Results.Unauthorized();

        var orderRepository = http.RequestServices.GetRequiredService<IReadRepository<Order>>();
        var notificationRepository = http.RequestServices.GetRequiredService<IRepository<SmsNotification>>();
        var notificationService = http.RequestServices.GetRequiredService<IOrderNotificationService>();

        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(ownerId), http.RequestAborted);

        var response = new MyOrdersResponse();
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            var notifications = await notificationRepository.ListAsync(new NotificationsByOrderSpecification(order.Id), http.RequestAborted);
            await notificationService.RefreshDeliveryStateAsync(notifications, http.RequestAborted);

            response.Orders.Add(new MyOrderDto(
                order.Id,
                order.OrderDate,
                order.Total(),
                order.OrderItems.Count,
                notifications.Select(NotificationView.From).ToList()));
        }

        return Results.Ok(response);
    }
}
