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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the signed-in shopper's orders, each showing where its notifications got to.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IRepository<Order>, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal claimsPrincipal, IRepository<Order> orderRepository, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(claimsPrincipal, orderRepository, notificationService);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal claimsPrincipal, IRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        var buyerId = claimsPrincipal.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await orderRepository.ListAsync(new OrdersByBuyerSpecification(buyerId));

        var response = new ListMyOrdersResponse();
        foreach (var order in orders)
        {
            var notifications = await notificationService.ListForOrderAsync(order.Id);
            response.Orders.Add(new OrderSummaryDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Notifications = notifications.Select(NotificationMapping.ToDto).ToList()
            });
        }

        return Results.Ok(response);
    }
}

public static class NotificationMapping
{
    public static NotificationDto ToDto(ApplicationCore.Entities.NotificationAggregate.OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        ProviderMessageSid = notification.ProviderMessageSid,
        NotificationType = notification.NotificationType.ToString(),
        Status = notification.Status,
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        Body = notification.ContentRedacted ? null : notification.Body,
        ContentRedacted = notification.ContentRedacted,
        ScheduledFor = notification.ScheduledFor,
        ResendOfNotificationId = notification.ResendOfNotificationId,
        CreatedAt = notification.CreatedAt,
        UpdatedAt = notification.UpdatedAt
    };
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
