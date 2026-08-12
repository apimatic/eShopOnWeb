using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The signed-in shopper's own orders, each showing where its notifications got to (delivery outcomes are
/// refreshed from the provider's current word). Only the caller's own orders are ever returned.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                IReadRepository<Order> orderRepository,
                IOrderNotificationService notifications,
                CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);

                var result = new List<MyOrderDto>(orders.Count);
                foreach (var order in orders)
                {
                    var orderNotifications = await notifications.GetOrderNotificationsAsync(order.Id, ct);
                    result.Add(new MyOrderDto(
                        order.Id,
                        order.OrderDate,
                        order.Total(),
                        order.Status.ToString(),
                        orderNotifications.Select(NotificationDto.From).ToList()));
                }

                return Results.Ok(new ListMyOrdersResponse(result));
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }
}

public record MyOrderDto(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Status,
    IReadOnlyList<NotificationDto> Notifications);

public record ListMyOrdersResponse(IReadOnlyList<MyOrderDto> Orders);
