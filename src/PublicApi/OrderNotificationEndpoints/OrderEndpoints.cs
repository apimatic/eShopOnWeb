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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed record PlaceOrderLineRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);
public sealed record PlaceOrderRequest(IReadOnlyList<PlaceOrderLineRequest> Items, ShippingAddressRequest? ShippingAddress);
public sealed record PlaceOrderResponse(int OrderId);
public sealed record OrderActionResponse(int OrderId, string Status);

public sealed class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    PlaceOrderRequest request,
                    ClaimsPrincipal principal,
                    CatalogContext context,
                    IOrderNotificationService notificationService,
                    CancellationToken cancellationToken) =>
                {
                    var buyerId = principal.Identity?.Name;
                    if (string.IsNullOrWhiteSpace(buyerId)) return Results.Unauthorized();
                    if (request.Items is null || request.Items.Count == 0)
                        return Results.BadRequest(new { error = "At least one order item is required." });
                    if (request.Items.Any(item => item.CatalogItemId <= 0 || item.Quantity <= 0))
                        return Results.BadRequest(new { error = "Catalog item ids and quantities must be positive." });
                    if (request.Items.GroupBy(item => item.CatalogItemId).Any(group => group.Count() > 1))
                        return Results.BadRequest(new { error = "Each catalog item may appear only once." });

                    var itemIds = request.Items.Select(item => item.CatalogItemId).ToArray();
                    var catalogItems = await context.CatalogItems
                        .AsNoTracking()
                        .Where(item => itemIds.Contains(item.Id))
                        .ToDictionaryAsync(item => item.Id, cancellationToken);
                    if (catalogItems.Count != itemIds.Length)
                        return Results.BadRequest(new { error = "One or more catalog items do not exist." });

                    var orderItems = request.Items.Select(line =>
                    {
                        var catalogItem = catalogItems[line.CatalogItemId];
                        return new OrderItem(
                            new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                            catalogItem.Price,
                            line.Quantity);
                    }).ToList();

                    var supplied = request.ShippingAddress;
                    var address = supplied is null
                        ? new Address("Not supplied", "Not supplied", string.Empty, "Not supplied", "Not supplied")
                        : new Address(supplied.Street, supplied.City, supplied.State, supplied.Country, supplied.ZipCode);
                    var order = new Order(buyerId, address, orderItems);
                    context.Orders.Add(order);
                    await context.SaveChangesAsync(cancellationToken);

                    try
                    {
                        await notificationService.SendOrderEventAsync(
                            order, NotificationKind.OrderPlaced, null, cancellationToken);
                    }
                    catch
                    {
                        // Notification infrastructure never rolls back an order that was placed.
                    }

                    return Results.Created($"/api/orders/{order.Id}", new PlaceOrderResponse(order.Id));
                })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderNotificationEndpoints");
    }
}

public sealed class DispatchOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    int orderId,
                    CatalogContext context,
                    IOrderNotificationService notificationService,
                    TimeProvider timeProvider,
                    CancellationToken cancellationToken) =>
                {
                    var order = await context.Orders.FindAsync(new object[] { orderId }, cancellationToken);
                    if (order is null) return Results.NotFound();

                    bool changed;
                    try
                    {
                        changed = order.Dispatch(timeProvider.GetUtcNow());
                    }
                    catch (InvalidOperationException exception)
                    {
                        return Results.Conflict(new { error = exception.Message });
                    }

                    if (changed)
                    {
                        await context.SaveChangesAsync(cancellationToken);
                        try
                        {
                            await notificationService.SendOrderEventAsync(
                                order, NotificationKind.OrderDispatched, null, cancellationToken);
                        }
                        catch { }
                        try
                        {
                            await notificationService.SendOrderEventAsync(
                                order,
                                NotificationKind.DeliveryFollowUp,
                                timeProvider.GetUtcNow().AddDays(3),
                                cancellationToken);
                        }
                        catch { }
                    }

                    return Results.Ok(new OrderActionResponse(order.Id, order.Status.ToString()));
                })
            .Produces<OrderActionResponse>()
            .WithTags("OrderNotificationEndpoints");
    }
}

public sealed class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    int orderId,
                    CatalogContext context,
                    IOrderNotificationService notificationService,
                    TimeProvider timeProvider,
                    CancellationToken cancellationToken) =>
                {
                    var order = await context.Orders.FindAsync(new object[] { orderId }, cancellationToken);
                    if (order is null) return Results.NotFound();

                    var changed = order.Cancel(timeProvider.GetUtcNow());
                    if (changed)
                    {
                        var followUps = await context.OrderNotifications.Where(notification =>
                            notification.OrderId == order.Id &&
                            notification.Kind == NotificationKind.DeliveryFollowUp &&
                            notification.ProviderStatus != NotificationDeliveryStatus.Canceled)
                            .ToListAsync(cancellationToken);
                        foreach (var notification in followUps)
                        {
                            notification.RequestCancellation(timeProvider.GetUtcNow());
                        }
                        await context.SaveChangesAsync(cancellationToken);

                        try
                        {
                            await notificationService.CancelOutstandingScheduledMessagesAsync(cancellationToken);
                        }
                        catch { }
                        try
                        {
                            await notificationService.SendOrderEventAsync(
                                order, NotificationKind.OrderCancelled, null, cancellationToken);
                        }
                        catch { }
                    }

                    return Results.Ok(new OrderActionResponse(order.Id, order.Status.ToString()));
                })
            .Produces<OrderActionResponse>()
            .WithTags("OrderNotificationEndpoints");
    }
}
