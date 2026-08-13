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
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

// ---------------------------------------------------------------------------------------------------
// Flow 2 — messages as the order moves. Placing an order and reading one's own orders are shopper-scoped;
// dispatch and cancel are operator (administrator) actions. A message that cannot be sent never fails the
// underlying operation — that guarantee lives in the notification service.
// ---------------------------------------------------------------------------------------------------

/// <summary>POST api/orders — place an order from catalog items for the signed-in shopper.</summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderNotificationService notificationService) =>
            {
                var buyerId = EndpointHelpers.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                request.BuyerId = buyerId;
                return await HandleAsync(request, notificationService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderNotificationService notificationService)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "An order must contain at least one item." });
        }

        var lines = request.Items
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();
        var shipToAddress = request.ShipToAddress?.ToAddress() ?? EndpointHelpers.DefaultShipToAddress();

        try
        {
            var orderId = await notificationService.PlaceOrderAsync(request.BuyerId, lines, shipToAddress);
            return Results.Created($"api/orders/{orderId}", new CreateOrderResponse(orderId));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}

/// <summary>POST api/orders/{orderId}/dispatch — operator marks the order dispatched.</summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, int, IRepository<Order>, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository, IOrderNotificationService notificationService) =>
                await HandleAsync(orderId, orderRepository, notificationService))
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        var order = await orderRepository.GetByIdAsync(orderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        await notificationService.DispatchOrderAsync(order);
        return Results.Ok(new OrderActionResponse(order.Id, "dispatched"));
    }
}

/// <summary>POST api/orders/{orderId}/cancel — operator cancels the order.</summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IRepository<Order>, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository, IOrderNotificationService notificationService) =>
                await HandleAsync(orderId, orderRepository, notificationService))
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        var order = await orderRepository.GetByIdAsync(orderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        await notificationService.CancelOrderAsync(order);
        return Results.Ok(new OrderActionResponse(order.Id, "cancelled"));
    }
}

/// <summary>GET api/my-orders — the caller's orders, each showing where its notifications got to.</summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IReadRepository<Order>, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IReadRepository<Order> orderRepository, IOrderNotificationService notificationService) =>
                await HandleAsync(user, orderRepository, notificationService))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IReadRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        var buyerId = EndpointHelpers.GetBuyerId(user);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));

        var orderDtos = new List<MyOrderDto>();
        foreach (var order in orders)
        {
            var notifications = await notificationService.GetNotificationsForOrderAsync(order.Id);
            orderDtos.Add(new MyOrderDto(
                order.Id,
                order.OrderDate,
                order.Total(),
                order.OrderItems.Select(i => new MyOrderItemDto(
                    i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList(),
                notifications.Select(NotificationDto.From).ToList()));
        }

        return Results.Ok(new MyOrdersResponse(orderDtos));
    }
}

/// <summary>GET api/orders/{orderId}/notifications — what was sent for this order, and its outcomes.</summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, IReadRepository<Order>, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IReadRepository<Order> orderRepository, IOrderNotificationService notificationService) =>
            {
                var buyerId = EndpointHelpers.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(new OrderNotificationsRequest(orderId, buyerId), orderRepository, notificationService);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, IReadRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId);

        // A shopper never sees another shopper's order — make it look absent.
        if (order is null || order.BuyerId != request.BuyerId)
        {
            return Results.NotFound();
        }

        var notifications = await notificationService.GetNotificationsForOrderAsync(order.Id);
        var response = new OrderNotificationsResponse(order.Id, notifications.Select(NotificationDto.From).ToList());
        return Results.Ok(response);
    }
}

// ----- DTOs -------------------------------------------------------------------------------------------

public class CreateOrderRequest
{
    /// <summary>Set from the token, never from the request body.</summary>
    public string BuyerId { get; set; } = string.Empty;

    public List<OrderItemRequest> Items { get; set; } = new();

    public AddressRequest? ShipToAddress { get; set; }
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;

    public Address ToAddress() => new(Street, City, State, Country, ZipCode);
}

public record CreateOrderResponse(int OrderId);

public record OrderActionResponse(int OrderId, string Status);

public record OrderNotificationsRequest(int OrderId, string BuyerId);

public record OrderNotificationsResponse(int OrderId, IReadOnlyList<NotificationDto> Notifications);

public record MyOrdersResponse(IReadOnlyList<MyOrderDto> Orders);

public record MyOrderDto(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<MyOrderItemDto> Items,
    IReadOnlyList<NotificationDto> Notifications);

public record MyOrderItemDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);
