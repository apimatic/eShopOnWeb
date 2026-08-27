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
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class OrderEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders", PlaceAsync)
            .RequireAuthorization(ShopperPolicy())
            .Produces(StatusCodes.Status201Created)
            .WithTags("OrderNotificationEndpoints");

        app.MapPost("api/orders/{orderId:int}/dispatch", DispatchAsync)
            .RequireAuthorization(OperatorPolicy())
            .WithTags("OrderNotificationEndpoints");

        app.MapPost("api/orders/{orderId:int}/cancel", CancelAsync)
            .RequireAuthorization(OperatorPolicy())
            .WithTags("OrderNotificationEndpoints");

        app.MapGet("api/my-orders", MyOrdersAsync)
            .RequireAuthorization(ShopperPolicy())
            .WithTags("OrderNotificationEndpoints");

        app.MapGet("api/orders/{orderId:int}/notifications", NotificationsAsync)
            .RequireAuthorization(ShopperPolicy())
            .WithTags("OrderNotificationEndpoints");
    }

    private static async Task<IResult> PlaceAsync(
        PlaceOrderRequest request,
        ClaimsPrincipal user,
        OrderNotificationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var address = request.ShippingAddress is null
                ? new Address("Not provided", "Not provided", string.Empty, "Not provided", "Not provided")
                : request.ShippingAddress.ToAddress();
            var lines = request.Items?.Select(x => new OrderLineInput(x.CatalogItemId, x.Quantity)).ToList()
                ?? new List<OrderLineInput>();
            var order = await service.PlaceOrderAsync(UserName(user), lines, address, cancellationToken);
            return Results.Created($"/api/orders/{order.Id}", new { orderId = order.Id });
        }
        catch (Exception exception)
        {
            return EndpointProblem.From(exception);
        }
    }

    private static Task<IResult> DispatchAsync(
        int orderId,
        OrderNotificationService service,
        CancellationToken cancellationToken) =>
        RunOperatorActionAsync(orderId, "dispatched", service.DispatchOrderAsync, cancellationToken);

    private static Task<IResult> CancelAsync(
        int orderId,
        OrderNotificationService service,
        CancellationToken cancellationToken) =>
        RunOperatorActionAsync(orderId, "cancelled", service.CancelOrderAsync, cancellationToken);

    private static async Task<IResult> RunOperatorActionAsync(
        int orderId,
        string status,
        Func<int, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await action(orderId, cancellationToken);
            return Results.Ok(new { orderId, status });
        }
        catch (Exception exception)
        {
            return EndpointProblem.From(exception);
        }
    }

    private static async Task<IResult> MyOrdersAsync(
        ClaimsPrincipal user,
        OrderNotificationService service,
        CancellationToken cancellationToken)
    {
        var buyerId = UserName(user);
        var orders = await service.GetOrdersForBuyerAsync(buyerId, cancellationToken);
        var notifications = await service.GetNotificationsForBuyerAsync(buyerId, cancellationToken);
        var byOrder = notifications.GroupBy(x => x.OrderId).ToDictionary(x => x.Key, x => x.ToList());

        return Results.Ok(orders.Select(order => new
        {
            orderId = order.Id,
            orderDate = order.OrderDate,
            status = order.Status.ToString(),
            dispatchedAt = order.DispatchedAt,
            cancelledAt = order.CancelledAt,
            total = order.Total(),
            items = order.OrderItems.Select(x => new
            {
                catalogItemId = x.ItemOrdered.CatalogItemId,
                productName = x.ItemOrdered.ProductName,
                quantity = x.Units,
                unitPrice = x.UnitPrice
            }),
            notifications = byOrder.GetValueOrDefault(order.Id, new List<OrderNotification>()).Select(NotificationView)
        }));
    }

    private static async Task<IResult> NotificationsAsync(
        int orderId,
        ClaimsPrincipal user,
        OrderNotificationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var notifications = await service.GetNotificationsForBuyerOrderAsync(
                UserName(user),
                orderId,
                cancellationToken);
            return Results.Ok(notifications.Select(NotificationView));
        }
        catch (Exception exception)
        {
            return EndpointProblem.From(exception);
        }
    }

    internal static object NotificationView(OrderNotification notification) => new
    {
        notificationId = notification.Id,
        kind = notification.Kind.ToString(),
        content = notification.Body,
        contentDisposedAt = notification.ContentDisposedAt,
        providerMessageSid = notification.ProviderMessageSid,
        status = notification.ProviderStatus,
        errorCode = notification.ProviderErrorCode,
        errorMessage = notification.ProviderErrorMessage,
        createdAt = notification.CreatedAt,
        statusUpdatedAt = notification.StatusUpdatedAt,
        scheduledFor = notification.ScheduledFor,
        resendOfNotificationId = notification.ResendOfNotificationId
    };

    private static AuthorizeAttribute ShopperPolicy() => new()
    {
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
    };

    private static AuthorizeAttribute OperatorPolicy() => new()
    {
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme,
        Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS
    };

    private static string UserName(ClaimsPrincipal user) =>
        user.Identity?.Name ?? throw new UnauthorizedAccessException();
}

public sealed class PlaceOrderRequest
{
    public List<PlaceOrderItemRequest>? Items { get; set; }
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public sealed record PlaceOrderItemRequest(int CatalogItemId, int Quantity);

public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode)
{
    public Address ToAddress() => new(Street, City, State, Country, ZipCode);
}
