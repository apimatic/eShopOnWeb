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
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public string? MessageSid { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public bool ContentRedacted { get; set; }

    /// <summary>The message text; null once its content has been disposed of.</summary>
    public string? Body { get; set; }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// Shows what was sent for one of the signed-in shopper's orders and what became of each
/// message, with delivery outcomes refreshed from the provider.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IReadRepository<OrderNotification> _notificationRepository;
    private readonly IOrderNotificationService _notificationService;

    public ListOrderNotificationsEndpoint(IReadRepository<Order> orderRepository,
        IReadRepository<OrderNotification> notificationRepository,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(orderId, user);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order is null || order.BuyerId != user.Identity!.Name)
        {
            return Results.NotFound();
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId));
        foreach (var notification in notifications)
        {
            await _notificationService.RefreshStatusAsync(notification);
        }

        var response = new ListOrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(n => new OrderNotificationDto
            {
                NotificationId = n.Id,
                Kind = n.Kind.ToString(),
                Status = n.Status,
                ErrorCode = n.ErrorCode,
                MessageSid = n.MessageSid,
                CreatedAt = n.CreatedAt,
                ScheduledFor = n.ScheduledFor,
                ContentRedacted = n.ContentRedacted,
                Body = n.Body
            }).ToList()
        };
        return Results.Ok(response);
    }
}
