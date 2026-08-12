using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationsApi;

// ------------------------------------------------------------------------------------
// Flow 2 — messages as the order moves. POST /api/orders is shopper-scoped; dispatch and
// cancel are operator (administrator) actions; the reads are the caller's own data.
// ------------------------------------------------------------------------------------

public record PlaceOrderRequestItem(int CatalogItemId, int Quantity);
public record PlaceOrderRequest(List<PlaceOrderRequestItem> Items);

/// <summary>POST /api/orders — place an order from catalog items for the signed-in shopper.</summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, ClaimsPrincipal user,
                   IOrderNotificationService service, CancellationToken ct) =>
            {
                var buyerId = user.GetUserId();
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                if (request?.Items is null || request.Items.Count == 0)
                {
                    return Results.BadRequest(new { error = "At least one order item is required." });
                }

                var lines = request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
                try
                {
                    var order = await service.PlaceOrderAsync(buyerId, lines, ct);
                    return Results.Created($"api/orders/{order.Id}", new
                    {
                        orderId = order.Id,
                        status = order.Status.ToString(),
                        total = order.Total()
                    });
                }
                catch (CatalogItemNotFoundException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Place an order and notify the shopper it was placed"));
    }
}

/// <summary>POST /api/orders/{orderId}/dispatch — operator marks the order dispatched.</summary>
public class DispatchOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderNotificationService service, CancellationToken ct) =>
            {
                try
                {
                    await service.DispatchOrderAsync(orderId, ct);
                    return Results.Ok(new { orderId, status = OrderStatus.Dispatched.ToString() });
                }
                catch (OrderNotFoundException) { return Results.NotFound(); }
                catch (System.InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Dispatch an order (operator)"));
    }
}

/// <summary>POST /api/orders/{orderId}/cancel — operator cancels the order.</summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderNotificationService service, CancellationToken ct) =>
            {
                try
                {
                    await service.CancelOrderAsync(orderId, ct);
                    return Results.Ok(new { orderId, status = OrderStatus.Cancelled.ToString() });
                }
                catch (OrderNotFoundException) { return Results.NotFound(); }
                catch (System.InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Cancel an order and call off any pending follow-up (operator)"));
    }
}

/// <summary>GET /api/my-orders — the caller's orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user,
                   IReadRepository<Order> orderRepo,
                   IRepository<OrderNotification> notificationRepo,
                   IOrderNotificationService service,
                   CancellationToken ct) =>
            {
                var buyerId = user.GetUserId();
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var orders = await orderRepo.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
                var orderIds = orders.Select(o => o.Id).ToArray();
                var notifications = orderIds.Length == 0
                    ? new List<OrderNotification>()
                    : (await notificationRepo.ListAsync(new OrderNotificationsByOrderIdsSpecification(orderIds), ct)).ToList();

                // No provider callbacks are possible here, so refresh non-terminal outcomes on read.
                await service.RefreshDeliveryStatusesAsync(notifications.Where(n => !MessageDeliveryStatus.IsTerminal(n.DeliveryStatus)), ct);

                var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());
                var result = orders.Select(o => new
                {
                    orderId = o.Id,
                    orderDate = o.OrderDate.ToString("o"),
                    status = o.Status.ToString(),
                    total = o.Total(),
                    items = o.OrderItems.Select(i => new { i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units }),
                    notifications = (byOrder.TryGetValue(o.Id, out var ns) ? ns : new List<OrderNotification>())
                        .Select(NotificationDto.From)
                }).ToList();

                return Results.Ok(new { orders = result });
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("OrderEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("List the caller's orders with notification statuses"));
    }
}

/// <summary>GET /api/orders/{orderId}/notifications — what was sent for this order, and what became of each message.</summary>
public class OrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, ClaimsPrincipal user,
                   IReadRepository<Order> orderRepo,
                   IRepository<OrderNotification> notificationRepo,
                   IOrderNotificationService service,
                   CancellationToken ct) =>
            {
                var callerId = user.GetUserId();
                if (string.IsNullOrEmpty(callerId)) return Results.Unauthorized();

                var order = await orderRepo.GetByIdAsync(orderId, ct);
                if (order is null) return Results.NotFound();

                // Shopper-scoped: a shopper only sees their own order. An operator (admin)
                // may see any order's notifications, since the operator endpoints act on the
                // notificationIds this endpoint returns.
                if (order.BuyerId != callerId && !user.IsAdministrator())
                {
                    return Results.NotFound();
                }

                var notifications = (await notificationRepo.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct)).ToList();
                await service.RefreshDeliveryStatusesAsync(notifications.Where(n => !MessageDeliveryStatus.IsTerminal(n.DeliveryStatus)), ct);

                return Results.Ok(new
                {
                    orderId,
                    notifications = notifications.Select(NotificationDto.From).ToList()
                });
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("List an order's notifications and their delivery outcomes"));
    }
}
