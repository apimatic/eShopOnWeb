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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// What was sent for an order and what became of each message. Delivery
/// outcomes are refreshed from the provider on read (no callback URL exists).
/// Shoppers see only their own orders; administrators see any.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IReadRepository<Order> orderRepository,
             IOrderNotificationService notificationService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(orderId, user, orderRepository, notificationService, cancellationToken);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user,
        IReadRepository<Order> orderRepository, IOrderNotificationService notificationService,
        CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(orderId, cancellationToken);
        var isAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        if (order is null || (!isAdmin && order.BuyerId != user.Identity!.Name))
        {
            return Results.NotFound();
        }

        var notifications = await notificationService.GetOrderNotificationsAsync(orderId, cancellationToken);

        var response = new ListOrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(n => new OrderNotificationDto
            {
                NotificationId = n.Id,
                Type = n.Type.ToString(),
                MessageSid = n.MessageSid,
                Status = n.Status,
                ErrorCode = n.ErrorCode,
                ErrorMessage = n.ErrorMessage,
                Body = n.Body,
                ContentRedacted = n.ContentRedacted,
                CreatedAt = n.CreatedAt,
                SentAt = n.SentAt,
                ScheduledFor = n.ScheduledFor
            }).ToList()
        };
        return Results.Ok(response);
    }
}
