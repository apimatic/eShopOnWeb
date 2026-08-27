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
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed record NotificationResponse(
    int NotificationId,
    string Kind,
    string Status,
    int? ProviderErrorCode,
    string? ProviderMessageSid,
    string? Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? SentAt,
    DateTimeOffset? ContentRedactedAt,
    int? OriginalNotificationId);

public sealed record MyOrderResponse(
    int OrderId,
    string Status,
    DateTimeOffset OrderedAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? CancelledAt,
    decimal Total,
    IReadOnlyList<NotificationResponse> Notifications);

public sealed class ListMyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    ClaimsPrincipal principal,
                    CatalogContext context,
                    IOrderNotificationService notificationService,
                    CancellationToken cancellationToken) =>
                {
                    var buyerId = principal.Identity?.Name;
                    if (string.IsNullOrWhiteSpace(buyerId)) return Results.Unauthorized();

                    var orders = await context.Orders
                        .AsNoTracking()
                        .Include(order => order.OrderItems)
                        .Where(order => order.BuyerId == buyerId)
                        .OrderByDescending(order => order.OrderDate)
                        .ToListAsync(cancellationToken);
                    var ids = orders.Select(order => order.Id).ToArray();
                    var notifications = await context.OrderNotifications
                        .Where(notification => ids.Contains(notification.OrderId))
                        .OrderBy(notification => notification.CreatedAt)
                        .ToListAsync(cancellationToken);
                    await notificationService.RefreshAsync(notifications, cancellationToken);

                    var response = orders.Select(order => new MyOrderResponse(
                        order.Id,
                        order.Status.ToString(),
                        order.OrderDate,
                        order.DispatchedAt,
                        order.CancelledAt,
                        order.Total(),
                        notifications.Where(notification => notification.OrderId == order.Id).Select(Map).ToList()))
                        .ToList();
                    return Results.Ok(response);
                })
            .Produces<MyOrderResponse[]>()
            .WithTags("OrderNotificationEndpoints");
    }

    internal static NotificationResponse Map(OrderNotification notification) => new(
        notification.Id,
        notification.Kind.ToString(),
        notification.ProviderStatus,
        notification.ProviderErrorCode,
        notification.ProviderMessageSid,
        notification.Body,
        notification.CreatedAt,
        notification.ScheduledFor,
        notification.ProviderSentAt,
        notification.ContentRedactedAt,
        notification.OriginalNotificationId);
}

public sealed class ListOrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    int orderId,
                    ClaimsPrincipal principal,
                    CatalogContext context,
                    IOrderNotificationService notificationService,
                    CancellationToken cancellationToken) =>
                {
                    var buyerId = principal.Identity?.Name;
                    if (string.IsNullOrWhiteSpace(buyerId)) return Results.Unauthorized();

                    var ownsOrder = await context.Orders.AnyAsync(
                        order => order.Id == orderId && order.BuyerId == buyerId, cancellationToken);
                    if (!ownsOrder) return Results.NotFound();

                    var notifications = await context.OrderNotifications
                        .Where(notification => notification.OrderId == orderId)
                        .OrderBy(notification => notification.CreatedAt)
                        .ToListAsync(cancellationToken);
                    await notificationService.RefreshAsync(notifications, cancellationToken);
                    return Results.Ok(notifications.Select(ListMyOrdersEndpoint.Map).ToList());
                })
            .Produces<NotificationResponse[]>()
            .WithTags("OrderNotificationEndpoints");
    }
}
