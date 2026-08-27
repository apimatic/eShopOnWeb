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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What was sent for an order, and what became of each message. Shopper-scoped: callers
/// see their own orders only (administrators may view any order's notifications).
/// Delivery outcomes are refreshed from the provider for messages not yet final.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IReadRepository<OrderNotification> _notificationRepository;
    private readonly IOrderNotificationService _notificationService;

    public ListOrderNotificationsEndpoint(
        IReadRepository<Order> orderRepository,
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
        var callerId = user.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(callerId))
        {
            return Results.Unauthorized();
        }

        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        var isAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        if (!isAdmin && order.BuyerId != callerId)
        {
            return Results.NotFound();
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId));
        foreach (var notification in notifications)
        {
            await _notificationService.SyncStatusAsync(notification);
        }

        var response = new ListOrderNotificationsResponse
        {
            OrderId = order.Id,
            Notifications = notifications.Select(n => new OrderNotificationDto
            {
                NotificationId = n.Id,
                Kind = n.Kind.ToString(),
                Status = n.ProviderStatus,
                ProviderMessageSid = n.ProviderMessageSid,
                ErrorCode = n.ProviderErrorCode,
                ErrorMessage = n.ProviderErrorMessage,
                Body = n.Body,
                ContentRedacted = n.ContentRedacted,
                CreatedAt = n.CreatedAt,
                ScheduledFor = n.ScheduledFor,
                LastSyncedAt = n.LastSyncedAt
            }).ToList()
        };
        return Results.Ok(response);
    }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>The message text; null once the content has been disposed of.</summary>
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
}
