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
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Places an order from catalog items for the signed-in shopper, reusing the existing order model.</summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                PlaceOrderRequest request,
                ClaimsPrincipal user,
                IOrderNotificationService service,
                CancellationToken cancellationToken) =>
            {
                var ownerId = user.GetUserId();
                if (string.IsNullOrEmpty(ownerId))
                {
                    return Results.Unauthorized();
                }
                if (request?.Items is null || request.Items.Count == 0)
                {
                    return Results.BadRequest(new { errors = new[] { "An order must contain at least one item." } });
                }

                var lines = request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
                var order = await service.PlaceOrderAsync(ownerId, lines, request.ToAddress(), cancellationToken);
                return Results.Created($"api/orders/{order.Id}", OrderDto.From(order));
            })
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }
}

/// <summary>Operator action: marks an order dispatched, notifies the shopper, and queues a delivery follow-up.</summary>
public class DispatchOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IOrderNotificationService service,
                CancellationToken cancellationToken) =>
            {
                var order = await service.DispatchAsync(orderId, cancellationToken);
                return Results.Ok(new OrderStatusResponse(order.Id, order.Status.ToString()));
            })
            .Produces<OrderStatusResponse>()
            .WithTags("OrderEndpoints");
    }
}

/// <summary>Operator action: cancels an order, notifies the shopper, and calls off any follow-up.</summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IOrderNotificationService service,
                CancellationToken cancellationToken) =>
            {
                var order = await service.CancelAsync(orderId, cancellationToken);
                return Results.Ok(new OrderStatusResponse(order.Id, order.Status.ToString()));
            })
            .Produces<OrderStatusResponse>()
            .WithTags("OrderEndpoints");
    }
}

/// <summary>Lists the caller's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                IOrderNotificationService service,
                CancellationToken cancellationToken) =>
            {
                var ownerId = user.GetUserId();
                if (string.IsNullOrEmpty(ownerId))
                {
                    return Results.Unauthorized();
                }

                var orders = await service.GetMyOrdersAsync(ownerId, cancellationToken);
                var orderIds = orders.Select(o => o.Id).ToList();
                var notifications = await service.GetNotificationsForOrdersAsync(orderIds, cancellationToken);
                var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

                var response = new MyOrdersResponse(orders.Select(order =>
                {
                    var dto = OrderDto.From(order);
                    var orderNotifications = byOrder.TryGetValue(order.Id, out var list)
                        ? list.Select(NotificationDto.From).ToList()
                        : new List<NotificationDto>();
                    return new MyOrderDto(dto, orderNotifications);
                }).ToList());

                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }
}

/// <summary>What was sent for one of the caller's own orders, and what became of each message.</summary>
public class OrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                ClaimsPrincipal user,
                IOrderNotificationService service,
                CancellationToken cancellationToken) =>
            {
                var ownerId = user.GetUserId();
                if (string.IsNullOrEmpty(ownerId))
                {
                    return Results.Unauthorized();
                }

                // Scope to the caller's own order so one shopper can never see another's.
                var order = await service.GetOwnedOrderAsync(ownerId, orderId, cancellationToken);
                if (order is null)
                {
                    return Results.NotFound();
                }

                var notifications = await service.GetNotificationsForOrdersAsync(new[] { orderId }, cancellationToken);
                var response = new OrderNotificationsResponse(orderId, notifications.Select(NotificationDto.From).ToList());
                return Results.Ok(response);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }
}

public record PlaceOrderItem(int CatalogItemId, int Quantity);

public record ShipToAddressDto(string Street, string City, string? State, string Country, string ZipCode);

public record PlaceOrderRequest(IReadOnlyList<PlaceOrderItem> Items, ShipToAddressDto? ShipToAddress)
{
    public Address ToAddress()
    {
        var a = ShipToAddress;
        return a is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "N/A")
            : new Address(a.Street, a.City, a.State ?? string.Empty, a.Country, a.ZipCode);
    }
}

public record OrderItemDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record OrderDto(int OrderId, string Status, System.DateTimeOffset OrderDate, decimal Total, IReadOnlyList<OrderItemDto> Items)
{
    public static OrderDto From(Order order) => new(
        order.Id,
        order.Status.ToString(),
        order.OrderDate,
        order.Total(),
        order.OrderItems.Select(i => new OrderItemDto(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList());
}

public record OrderStatusResponse(int OrderId, string Status);
public record MyOrderDto(OrderDto Order, IReadOnlyList<NotificationDto> Notifications);
public record MyOrdersResponse(IReadOnlyList<MyOrderDto> Orders);
public record OrderNotificationsResponse(int OrderId, IReadOnlyList<NotificationDto> Notifications);
