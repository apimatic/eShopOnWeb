using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<OrderDto> Orders { get; set; } = new();
}

/// <summary>
/// Lists the caller's own orders, each showing where its notifications got to. Delivery outcomes that
/// are not yet final are refreshed from the provider first, since there is no callback into this app.
/// </summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user,
             IReadRepository<Order> orderRepository,
             IRepository<SmsNotification> notificationRepository,
             ISmsNotificationService notificationService) =>
            {
                var buyerId = user.GetUserId();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
                var orderIds = orders.Select(o => o.Id).ToList();

                var notifications = orderIds.Count == 0
                    ? new List<SmsNotification>()
                    : (await notificationRepository.ListAsync(new SmsNotificationsByOrdersSpecification(orderIds))).ToList();

                // Bring non-final delivery outcomes up to date before reporting them.
                await notificationService.RefreshDeliveryOutcomesAsync(notifications);

                var byOrder = notifications.GroupBy(n => n.OrderId)
                    .ToDictionary(g => g.Key, g => g.Select(SmsNotificationDto.From).ToList());

                var response = new MyOrdersResponse();
                foreach (var order in orders)
                {
                    var dto = OrderDto.From(order);
                    if (byOrder.TryGetValue(order.Id, out var dtos))
                        dto.Notifications = dtos;
                    response.Orders.Add(dto);
                }

                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }
}
