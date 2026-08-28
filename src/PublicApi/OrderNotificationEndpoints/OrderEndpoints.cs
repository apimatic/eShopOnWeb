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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed record CreateOrderItemRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);
public sealed record CreateOrderRequest(IReadOnlyList<CreateOrderItemRequest>? Items, ShippingAddressRequest? ShippingAddress);

public sealed class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request,
                HttpContext httpContext,
                CatalogContext db,
                IUriComposer uriComposer,
                OrderNotificationService notifications,
                CancellationToken cancellationToken) =>
            {
                var validation = Validate(request);
                if (validation is not null)
                {
                    return validation;
                }

                var requested = request.Items!
                    .GroupBy(x => x.CatalogItemId)
                    .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
                var catalogItems = await db.CatalogItems
                    .Where(x => requested.Keys.Contains(x.Id))
                    .ToListAsync(cancellationToken);
                if (catalogItems.Count != requested.Count)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["items"] = new[] { "One or more catalog items do not exist." }
                    });
                }

                var orderItems = catalogItems.Select(item => new OrderItem(
                    new CatalogItemOrdered(item.Id, item.Name, uriComposer.ComposePicUri(item.PictureUri)),
                    item.Price,
                    requested[item.Id])).ToList();
                var address = request.ShippingAddress!;
                var order = new Order(
                    CurrentUser.BuyerId(httpContext),
                    new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
                    orderItems);

                db.Orders.Add(order);
                await db.SaveChangesAsync(cancellationToken);

                try
                {
                    await notifications.NotifyOrderPlacedAsync(order, cancellationToken);
                }
                catch
                {
                    // The durable order is the primary operation and always survives notification failure.
                }

                return Results.Created($"/api/orders/{order.Id}", new { orderId = order.Id });
            })
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .RequireAuthorization()
            .WithTags("OrderNotificationEndpoints");
    }

    private static IResult? Validate(CreateOrderRequest request)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["items"] = new[] { "At least one item is required." } });
        }

        if (request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0 || x.Quantity > 100))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["items"] = new[] { "Catalog item ids must be positive and quantities must be between 1 and 100." }
            });
        }

        var address = request.ShippingAddress;
        if (address is null ||
            string.IsNullOrWhiteSpace(address.Street) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) ||
            string.IsNullOrWhiteSpace(address.ZipCode))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["shippingAddress"] = new[] { "Street, city, country and zipCode are required." }
            });
        }

        return null;
    }
}

public sealed class ListMyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                HttpContext httpContext,
                CatalogContext db,
                OrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CurrentUser.BuyerId(httpContext);
                var orders = await db.Orders
                    .AsNoTracking()
                    .Include(x => x.OrderItems)
                    .Where(x => x.BuyerId == buyerId)
                    .OrderByDescending(x => x.OrderDate)
                    .ToListAsync(cancellationToken);
                var orderIds = orders.Select(x => x.Id).ToArray();
                var notificationEntities = await db.OrderNotifications
                    .Where(x => orderIds.Contains(x.OrderId) && x.BuyerId == buyerId)
                    .OrderBy(x => x.CreatedAt)
                    .ToListAsync(cancellationToken);
                await notificationService.RefreshAsync(notificationEntities, cancellationToken);
                var byOrder = notificationEntities.ToLookup(x => x.OrderId);

                return Results.Ok(orders.Select(order => new
                {
                    orderId = order.Id,
                    orderDate = order.OrderDate,
                    status = order.Status.ToString(),
                    total = order.Total(),
                    items = order.OrderItems.Select(x => new
                    {
                        catalogItemId = x.ItemOrdered.CatalogItemId,
                        productName = x.ItemOrdered.ProductName,
                        quantity = x.Units,
                        unitPrice = x.UnitPrice
                    }),
                    notifications = byOrder[order.Id].Select(NotificationDto.FromEntity)
                }));
            })
            .RequireAuthorization()
            .WithTags("OrderNotificationEndpoints");
    }
}

public sealed class GetOrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                HttpContext httpContext,
                CatalogContext db,
                OrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CurrentUser.BuyerId(httpContext);
                if (!await db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken))
                {
                    return Results.NotFound();
                }

                var notifications = await db.OrderNotifications
                    .Where(x => x.OrderId == orderId && x.BuyerId == buyerId)
                    .OrderBy(x => x.CreatedAt)
                    .ToListAsync(cancellationToken);
                await notificationService.RefreshAsync(notifications, cancellationToken);
                return Results.Ok(notifications.Select(NotificationDto.FromEntity));
            })
            .RequireAuthorization()
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
                CatalogContext db,
                OrderNotificationService notifications,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
                if (order is null)
                {
                    return Results.NotFound();
                }

                bool changed;
                try
                {
                    changed = order.Dispatch(timeProvider.GetUtcNow());
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new { message = ex.Message });
                }

                if (changed)
                {
                    await db.SaveChangesAsync(cancellationToken);
                    try
                    {
                        await notifications.NotifyOrderDispatchedAsync(order, cancellationToken);
                    }
                    catch
                    {
                        // Dispatch is committed even if notification persistence or delivery fails.
                    }
                }

                return Results.Ok(new { orderId = order.Id, status = order.Status.ToString() });
            })
            .RequireAuthorization()
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
                CatalogContext db,
                OrderNotificationService notifications,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
                if (order is null)
                {
                    return Results.NotFound();
                }

                var changed = order.Cancel(timeProvider.GetUtcNow());
                if (changed)
                {
                    await db.SaveChangesAsync(cancellationToken);
                    try
                    {
                        await notifications.NotifyOrderCancelledAsync(order, cancellationToken);
                    }
                    catch
                    {
                        // Cancellation is committed; persisted cancellation requests are retried by the worker.
                    }
                }
                else
                {
                    await notifications.RetryRequestedCancellationsAsync(cancellationToken);
                }

                return Results.Ok(new { orderId = order.Id, status = order.Status.ToString() });
            })
            .RequireAuthorization()
            .WithTags("OrderNotificationEndpoints");
    }
}
