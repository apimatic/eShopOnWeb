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
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<Order> _orders;
    private readonly IOrderNotificationService _notifications;
    private readonly IUriComposer _uriComposer;

    public CreateOrderEndpoint(
        IRepository<CatalogItem> catalogItems,
        IRepository<Order> orders,
        IOrderNotificationService notifications,
        IUriComposer uriComposer)
    {
        _catalogItems = catalogItems;
        _orders = orders;
        _notifications = notifications;
        _uriComposer = uriComposer;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                await HandleAsync(request, user, cancellationToken))
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateOrderRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        if (request.Items.Count == 0 || request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
        {
            return Results.BadRequest(new { error = "At least one catalog item with a positive quantity is required." });
        }

        if (!ValidAddress(request.ShippingAddress))
        {
            return Results.BadRequest(new { error = "A complete shippingAddress is required." });
        }

        var requested = request.Items
            .GroupBy(x => x.CatalogItemId)
            .Select(x => new { CatalogItemId = x.Key, Quantity = x.Sum(i => i.Quantity) })
            .ToArray();
        var catalog = await _catalogItems.ListAsync(
            new CatalogItemsSpecification(requested.Select(x => x.CatalogItemId).ToArray()),
            cancellationToken);
        if (catalog.Count != requested.Length)
        {
            return Results.BadRequest(new { error = "One or more catalog item ids do not exist." });
        }

        var byId = catalog.ToDictionary(x => x.Id);
        var orderItems = requested.Select(item =>
        {
            var catalogItem = byId[item.CatalogItemId];
            return new OrderItem(
                new CatalogItemOrdered(
                    catalogItem.Id,
                    catalogItem.Name,
                    _uriComposer.ComposePicUri(catalogItem.PictureUri)),
                catalogItem.Price,
                item.Quantity);
        }).ToList();
        var address = request.ShippingAddress;
        var order = new Order(
            RegisterContactNumberEndpoint.BuyerId(user),
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            orderItems);
        await _orders.AddAsync(order, cancellationToken);
        await _notifications.NotifyOrderPlacedAsync(order, cancellationToken);

        return Results.Created($"/api/orders/{order.Id}", new { orderId = order.Id });
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user) =>
        HandleAsync(request, user, CancellationToken.None);

    private static bool ValidAddress(ShippingAddressRequest address) =>
        !string.IsNullOrWhiteSpace(address.Street) &&
        !string.IsNullOrWhiteSpace(address.City) &&
        !string.IsNullOrWhiteSpace(address.Country) &&
        !string.IsNullOrWhiteSpace(address.ZipCode);
}

public sealed class DispatchOrderEndpoint : IEndpoint<IResult, int>
{
    private readonly IRepository<Order> _orders;
    private readonly IOrderNotificationService _notifications;

    public DispatchOrderEndpoint(IRepository<Order> orders, IOrderNotificationService notifications)
    {
        _orders = orders;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CancellationToken cancellationToken) => await HandleAsync(orderId, cancellationToken))
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return Results.NotFound();
        }

        if (!order.Dispatch(DateTimeOffset.UtcNow))
        {
            return Results.Conflict(new { error = $"An order in state {order.Status} cannot be dispatched." });
        }

        await _orders.UpdateAsync(order, cancellationToken);
        await _notifications.NotifyOrderDispatchedAsync(order, cancellationToken);
        return Results.Ok(new { orderId = order.Id, status = order.Status.ToString() });
    }

    public Task<IResult> HandleAsync(int orderId) => HandleAsync(orderId, CancellationToken.None);
}

public sealed class CancelOrderEndpoint : IEndpoint<IResult, int>
{
    private readonly IRepository<Order> _orders;
    private readonly IOrderNotificationService _notifications;

    public CancelOrderEndpoint(IRepository<Order> orders, IOrderNotificationService notifications)
    {
        _orders = orders;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CancellationToken cancellationToken) => await HandleAsync(orderId, cancellationToken))
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return Results.NotFound();
        }

        if (!order.Cancel(DateTimeOffset.UtcNow))
        {
            return Results.Conflict(new { error = "The order is already cancelled." });
        }

        await _orders.UpdateAsync(order, cancellationToken);
        await _notifications.NotifyOrderCancelledAsync(order, cancellationToken);
        return Results.Ok(new { orderId = order.Id, status = order.Status.ToString() });
    }

    public Task<IResult> HandleAsync(int orderId) => HandleAsync(orderId, CancellationToken.None);
}

public sealed class ListMyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orders;
    private readonly IOrderNotificationService _notifications;

    public ListMyOrdersEndpoint(IRepository<Order> orders, IOrderNotificationService notifications)
    {
        _orders = orders;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, CancellationToken cancellationToken) => await HandleAsync(user, cancellationToken))
            .Produces(StatusCodes.Status200OK)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var buyerId = RegisterContactNumberEndpoint.BuyerId(user);
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var response = new List<object>();
        foreach (var order in orders.OrderByDescending(x => x.OrderDate))
        {
            var notifications = await _notifications.GetOrderNotificationsAsync(order.Id, buyerId, cancellationToken);
            response.Add(new
            {
                orderId = order.Id,
                orderDate = order.OrderDate,
                status = order.Status.ToString(),
                total = order.Total(),
                notificationOutcomes = notifications.Select(NotificationDto)
            });
        }

        return Results.Ok(new { orders = response });
    }

    public Task<IResult> HandleAsync(ClaimsPrincipal user) => HandleAsync(user, CancellationToken.None);

    internal static object NotificationDto(OrderNotification notification) => new
    {
        notificationId = notification.Id,
        kind = notification.Kind.ToString(),
        content = notification.Content,
        contentDisposedAt = notification.ContentDisposedAt,
        providerMessageId = notification.ProviderMessageId,
        providerStatus = notification.ProviderStatus,
        providerErrorCode = notification.ProviderErrorCode,
        providerErrorMessage = notification.ProviderErrorMessage,
        scheduledFor = notification.ScheduledFor,
        createdAt = notification.CreatedAt,
        sentAt = notification.ProviderSentAt,
        lastRefreshedAt = notification.LastRefreshedAt,
        refreshDiagnostic = notification.RefreshDiagnostic
    };
}

public sealed class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orders;
    private readonly IOrderNotificationService _notifications;

    public ListOrderNotificationsEndpoint(IRepository<Order> orders, IOrderNotificationService notifications)
    {
        _orders = orders;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                await HandleAsync(orderId, user, cancellationToken))
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(
        int orderId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var buyerId = RegisterContactNumberEndpoint.BuyerId(user);
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        var notifications = await _notifications.GetOrderNotificationsAsync(orderId, buyerId, cancellationToken);
        return Results.Ok(new
        {
            orderId,
            notifications = notifications.Select(ListMyOrdersEndpoint.NotificationDto)
        });
    }

    public Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user) =>
        HandleAsync(orderId, user, CancellationToken.None);
}
