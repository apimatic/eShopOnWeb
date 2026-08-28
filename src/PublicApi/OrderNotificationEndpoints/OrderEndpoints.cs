using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed class OrderEndpoints : IEndpoint
{
    private const string AuthenticationScheme = JwtBearerDefaults.AuthenticationScheme;
    private const string AdministratorRole = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = AuthenticationScheme)] async (
                PlaceOrderRequest request,
                HttpContext httpContext,
                CatalogContext db,
                OrderNotificationManager notificationManager,
                ILogger<OrderEndpoints> logger,
                CancellationToken cancellationToken) =>
            {
                var validationErrors = Validate(request);
                if (validationErrors.Count > 0)
                {
                    return Results.ValidationProblem(validationErrors);
                }

                var requestedItems = request.Items!
                    .GroupBy(x => x.CatalogItemId)
                    .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
                var catalogItems = await db.CatalogItems
                    .Where(x => requestedItems.Keys.Contains(x.Id))
                    .ToListAsync(cancellationToken);
                var missingIds = requestedItems.Keys.Except(catalogItems.Select(x => x.Id)).OrderBy(x => x).ToArray();
                if (missingIds.Length > 0)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["items"] = new[] { $"Catalog items do not exist: {string.Join(", ", missingIds)}." } });
                }

                var orderItems = catalogItems.Select(item => new OrderItem(
                    new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
                    item.Price,
                    requestedItems[item.Id])).ToList();
                var address = request.ShipToAddress!;
                var order = new Order(
                    httpContext.User.Identity!.Name!,
                    new Address(address.Street!, address.City!, address.State ?? string.Empty, address.Country!, address.ZipCode!),
                    orderItems);
                db.Orders.Add(order);
                await db.SaveChangesAsync(cancellationToken);

                try
                {
                    await notificationManager.NotifyOrderPlacedAsync(order, cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Notifications for newly placed order {OrderId} could not be processed.", order.Id);
                }

                return Results.Created($"/api/orders/{order.Id}", new PlaceOrderResponse(order.Id));
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .RequireAuthorization()
            .WithTags("OrderNotificationEndpoints");

        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = AdministratorRole, AuthenticationSchemes = AuthenticationScheme)] async (
                int orderId,
                CatalogContext db,
                OrderNotificationManager notificationManager,
                TimeProvider clock,
                ILogger<OrderEndpoints> logger,
                CancellationToken cancellationToken) =>
            {
                var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
                if (order is null) return Results.NotFound();
                if (order.Status == OrderStatus.Cancelled) return Results.Conflict(new { message = "A cancelled order cannot be dispatched." });
                if (order.Status == OrderStatus.Dispatched) return Results.Ok(new OrderTransitionResponse(order.Id, order.Status.ToString()));

                order.MarkDispatched(clock.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
                try
                {
                    await notificationManager.NotifyOrderDispatchedAsync(order, cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Notifications for dispatched order {OrderId} could not be processed.", order.Id);
                }
                return Results.Ok(new OrderTransitionResponse(order.Id, order.Status.ToString()));
            })
            .RequireAuthorization()
            .WithTags("OrderNotificationEndpoints");

        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = AdministratorRole, AuthenticationSchemes = AuthenticationScheme)] async (
                int orderId,
                CatalogContext db,
                OrderNotificationManager notificationManager,
                TimeProvider clock,
                ILogger<OrderEndpoints> logger,
                CancellationToken cancellationToken) =>
            {
                var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
                if (order is null) return Results.NotFound();
                if (order.Status == OrderStatus.Cancelled) return Results.Ok(new OrderTransitionResponse(order.Id, order.Status.ToString()));

                order.Cancel(clock.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
                try
                {
                    await notificationManager.RequestCancellationForOrderAsync(order.Id, cancellationToken);
                    await notificationManager.NotifyOrderCancelledAsync(order, cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Notifications for cancelled order {OrderId} could not be processed.", order.Id);
                }
                return Results.Ok(new OrderTransitionResponse(order.Id, order.Status.ToString()));
            })
            .RequireAuthorization()
            .WithTags("OrderNotificationEndpoints");

        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = AuthenticationScheme)] async (
                HttpContext httpContext,
                CatalogContext db,
                OrderNotificationManager notificationManager,
                CancellationToken cancellationToken) =>
            {
                var buyerId = httpContext.User.Identity!.Name!;
                var orders = await db.Orders
                    .Where(x => x.BuyerId == buyerId)
                    .Include(x => x.OrderItems)
                    .OrderByDescending(x => x.OrderDate)
                    .ToListAsync(cancellationToken);
                var result = new List<MyOrderResponse>();
                foreach (var order in orders)
                {
                    var notifications = await notificationManager.RefreshForOrderAsync(order.Id, cancellationToken);
                    result.Add(ToOrderResponse(order, notifications));
                }
                return Results.Ok(new MyOrdersResponse(result));
            })
            .RequireAuthorization()
            .WithTags("OrderNotificationEndpoints");

        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = AuthenticationScheme)] async (
                int orderId,
                HttpContext httpContext,
                CatalogContext db,
                OrderNotificationManager notificationManager,
                CancellationToken cancellationToken) =>
            {
                var buyerId = httpContext.User.Identity!.Name!;
                var ownsOrder = await db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken);
                if (!ownsOrder) return Results.NotFound();
                var notifications = await notificationManager.RefreshForOrderAsync(orderId, cancellationToken);
                return Results.Ok(new OrderNotificationsResponse(orderId, notifications.Select(ToNotificationResponse).ToList()));
            })
            .RequireAuthorization()
            .WithTags("OrderNotificationEndpoints");
    }

    private static Dictionary<string, string[]> Validate(PlaceOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Items is null || request.Items.Count == 0)
            errors["items"] = new[] { "At least one catalog item is required." };
        else if (request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            errors["items"] = new[] { "Catalog item ids and quantities must be positive." };

        var address = request.ShipToAddress;
        if (address is null || string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City)
            || string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.ZipCode))
            errors["shipToAddress"] = new[] { "Street, city, country and zipCode are required." };
        return errors;
    }

    private static MyOrderResponse ToOrderResponse(Order order, IReadOnlyList<OrderNotification> notifications) => new(
        order.Id,
        order.OrderDate,
        order.Status.ToString(),
        order.Total(),
        notifications.Select(ToNotificationResponse).ToList());

    internal static NotificationResponse ToNotificationResponse(OrderNotification notification) => new(
        notification.Id,
        notification.Type.ToString(),
        notification.Body,
        notification.ProviderMessageSid,
        notification.ProviderStatus,
        notification.ProviderErrorCode,
        notification.CreatedAt,
        notification.ScheduledFor,
        notification.ProviderDateSent,
        notification.LastCheckedAt,
        notification.ContentRedactedAt,
        notification.ResendOfNotificationId);
}

public sealed class PlaceOrderRequest
{
    public List<PlaceOrderItemRequest>? Items { get; set; }
    public ShippingAddressRequest? ShipToAddress { get; set; }
}

public sealed record PlaceOrderItemRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string? Street, string? City, string? State, string? Country, string? ZipCode);
public sealed record PlaceOrderResponse(int OrderId);
public sealed record OrderTransitionResponse(int OrderId, string Status);
public sealed record MyOrdersResponse(IReadOnlyList<MyOrderResponse> Orders);
public sealed record MyOrderResponse(int OrderId, DateTimeOffset OrderDate, string Status, decimal Total, IReadOnlyList<NotificationResponse> Notifications);
public sealed record OrderNotificationsResponse(int OrderId, IReadOnlyList<NotificationResponse> Notifications);
public sealed record NotificationResponse(
    int NotificationId,
    string Type,
    string? Body,
    string? ProviderMessageSid,
    string ProviderStatus,
    int? ProviderErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? ProviderDateSent,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset? ContentRedactedAt,
    int? ResendOfNotificationId);
