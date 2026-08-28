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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Twilio;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public static class OrderNotificationEndpoints
{
    private const string AdministratorRole = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    public static IEndpointRouteBuilder MapOrderNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var shopper = new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme };
        var administrator = new AuthorizeAttribute
        {
            AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme,
            Roles = AdministratorRole
        };

        app.MapPost("/api/contact-numbers", RegisterContactNumberAsync)
            .RequireAuthorization(shopper).WithTags("ContactNumbers");
        app.MapGet("/api/contact-numbers", ListContactNumbersAsync)
            .RequireAuthorization(shopper).WithTags("ContactNumbers");
        app.MapDelete("/api/contact-numbers/{contactNumberId:int}", DeleteContactNumberAsync)
            .RequireAuthorization(shopper).WithTags("ContactNumbers");

        app.MapPost("/api/orders", PlaceOrderAsync)
            .RequireAuthorization(shopper).WithTags("Orders");
        app.MapPost("/api/orders/{orderId:int}/dispatch", DispatchOrderAsync)
            .RequireAuthorization(administrator).WithTags("Orders");
        app.MapPost("/api/orders/{orderId:int}/cancel", CancelOrderAsync)
            .RequireAuthorization(administrator).WithTags("Orders");
        app.MapGet("/api/my-orders", ListMyOrdersAsync)
            .RequireAuthorization(shopper).WithTags("Orders");
        app.MapGet("/api/orders/{orderId:int}/notifications", ListOrderNotificationsAsync)
            .RequireAuthorization(shopper).WithTags("OrderNotifications");

        app.MapPost("/api/notifications/{notificationId:int}/resend", ResendNotificationAsync)
            .RequireAuthorization(administrator).WithTags("OrderNotifications");
        app.MapDelete("/api/notifications/{notificationId:int}/content", DisposeNotificationContentAsync)
            .RequireAuthorization(administrator).WithTags("OrderNotifications");
        app.MapGet("/api/notifications/reconciliation", ReconcileNotificationsAsync)
            .RequireAuthorization(administrator).WithTags("OrderNotifications");

        return app;
    }

    private static async Task<IResult> RegisterContactNumberAsync(RegisterContactNumberRequest request,
        ClaimsPrincipal user, CatalogContext db, ITwilioGateway twilio, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return Validation("phoneNumber", "A phone number is required.");

        ValidatedPhoneNumber validated;
        try
        {
            validated = await twilio.ValidatePhoneNumberAsync(request.PhoneNumber, cancellationToken);
        }
        catch (Exception)
        {
            return Results.Problem("The phone number could not be validated by the messaging provider.", statusCode: 503);
        }

        if (!validated.IsValid || string.IsNullOrWhiteSpace(validated.CanonicalNumber))
            return Validation("phoneNumber", "The messaging provider does not consider this a valid destination.");

        var buyerId = BuyerId(user);
        var existing = await db.ContactNumbers.SingleOrDefaultAsync(x =>
            x.BuyerId == buyerId && x.CanonicalNumber == validated.CanonicalNumber, cancellationToken);
        if (existing is not null) return Results.Ok(new ContactNumberCreatedResponse(existing.Id));

        var contact = new ContactNumber(buyerId, validated.CanonicalNumber);
        db.ContactNumbers.Add(contact);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.Entry(contact).State = EntityState.Detached;
            existing = await db.ContactNumbers.SingleOrDefaultAsync(x =>
                x.BuyerId == buyerId && x.CanonicalNumber == validated.CanonicalNumber, cancellationToken);
            if (existing is not null) return Results.Ok(new ContactNumberCreatedResponse(existing.Id));
            throw;
        }
        return Results.Created($"/api/contact-numbers/{contact.Id}", new ContactNumberCreatedResponse(contact.Id));
    }

    private static async Task<IResult> ListContactNumbersAsync(ClaimsPrincipal user, CatalogContext db,
        CancellationToken cancellationToken)
    {
        var buyerId = BuyerId(user);
        var contacts = await db.ContactNumbers.AsNoTracking().Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .Select(x => new ContactNumberResponse(x.Id, x.CanonicalNumber, x.CreatedAt))
            .ToListAsync(cancellationToken);
        return Results.Ok(contacts);
    }

    private static async Task<IResult> DeleteContactNumberAsync(int contactNumberId, ClaimsPrincipal user,
        CatalogContext db, CancellationToken cancellationToken)
    {
        var buyerId = BuyerId(user);
        var contact = await db.ContactNumbers.SingleOrDefaultAsync(x =>
            x.Id == contactNumberId && x.BuyerId == buyerId, cancellationToken);
        if (contact is null) return Results.NotFound();
        db.ContactNumbers.Remove(contact);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> PlaceOrderAsync(PlaceOrderRequest request, ClaimsPrincipal user,
        CatalogContext db, OrderNotificationService notifications, CancellationToken cancellationToken)
    {
        var errors = ValidateOrder(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var requested = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        var catalogItems = await db.CatalogItems.Where(x => requested.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var missing = requested.Keys.Except(catalogItems.Select(x => x.Id)).ToArray();
        if (missing.Length > 0)
            return Validation("items", $"Catalog item ids do not exist: {string.Join(", ", missing)}.");

        var address = request.ShippingAddress!;
        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, requested[item.Id])).ToList();
        var order = new Order(BuyerId(user),
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode), orderItems);
        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyOrderPlacedAsync(order);
        return Results.Created($"/api/orders/{order.Id}", new OrderCreatedResponse(order.Id));
    }

    private static async Task<IResult> DispatchOrderAsync(int orderId, CatalogContext db,
        OrderNotificationService notifications, CancellationToken cancellationToken)
    {
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) return Results.NotFound();
        if (!order.Dispatch(DateTimeOffset.UtcNow))
            return Results.Conflict(new { error = $"Order is already {order.Status.ToString().ToLowerInvariant()}." });

        await db.SaveChangesAsync(cancellationToken);
        await notifications.NotifyOrderDispatchedAsync(order);
        return Results.Ok(new OrderStateChangedResponse(order.Id, order.Status.ToString()));
    }

    private static async Task<IResult> CancelOrderAsync(int orderId, CatalogContext db,
        OrderNotificationService notifications, CancellationToken cancellationToken)
    {
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) return Results.NotFound();
        if (!order.Cancel(DateTimeOffset.UtcNow))
            return Results.Conflict(new { error = "Order is already cancelled." });

        await db.SaveChangesAsync(cancellationToken);
        await notifications.NotifyOrderCancelledAsync(order);
        return Results.Ok(new OrderStateChangedResponse(order.Id, order.Status.ToString()));
    }

    private static async Task<IResult> ListMyOrdersAsync(ClaimsPrincipal user, CatalogContext db,
        OrderNotificationService notifications, CancellationToken cancellationToken)
    {
        var buyerId = BuyerId(user);
        var orders = await db.Orders.AsNoTracking().Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId).OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToArray();
        var messages = await db.OrderNotifications.Where(x => orderIds.Contains(x.OrderId)).ToListAsync(cancellationToken);
        await notifications.RefreshAsync(messages);
        return Results.Ok(orders.Select(order => MapOrder(order,
            messages.Where(x => x.OrderId == order.Id).OrderBy(x => x.CreatedAt).ToList())).ToList());
    }

    private static async Task<IResult> ListOrderNotificationsAsync(int orderId, ClaimsPrincipal user,
        CatalogContext db, OrderNotificationService notifications, CancellationToken cancellationToken)
    {
        var buyerId = BuyerId(user);
        var ownsOrder = await db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken);
        if (!ownsOrder) return Results.NotFound();
        var messages = await db.OrderNotifications.Where(x => x.OrderId == orderId && x.BuyerId == buyerId)
            .OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);
        await notifications.RefreshAsync(messages);
        return Results.Ok(messages.Select(MapNotification).ToList());
    }

    private static async Task<IResult> ResendNotificationAsync(int notificationId,
        ResendNotificationRequest request, OrderNotificationService notifications)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 200)
            return Validation("idempotencyKey", "An idempotency key of at most 200 characters is required.");
        var result = await notifications.ResendAsync(notificationId, request.IdempotencyKey);
        if (result.Succeeded) return Results.Created($"/api/notifications/{result.NotificationId}",
            new NotificationCreatedResponse(result.NotificationId!.Value));
        return result.Error switch
        {
            "not-found" => Results.NotFound(),
            "contact-removed" => Results.Conflict(new { error = "The destination was removed and cannot be used again." }),
            _ => Results.Conflict(new { error = "Only an unsuccessful, retained message can be resent." })
        };
    }

    private static async Task<IResult> DisposeNotificationContentAsync(int notificationId,
        OrderNotificationService notifications)
    {
        var result = await notifications.DisposeContentAsync(notificationId);
        return result switch
        {
            ContentDisposalResult.Success => Results.NoContent(),
            ContentDisposalResult.NotFound => Results.NotFound(),
            _ => Results.Problem("The provider did not confirm content disposal; no local content was changed.", statusCode: 502)
        };
    }

    private static async Task<IResult> ReconcileNotificationsAsync(DateTimeOffset from, DateTimeOffset to,
        OrderNotificationService notifications, CancellationToken cancellationToken)
    {
        if (from >= to) return Validation("range", "'from' must be earlier than 'to'.");
        try
        {
            return Results.Ok(await notifications.ReconcileAsync(from, to, cancellationToken));
        }
        catch (Exception)
        {
            return Results.Problem("The provider reconciliation query failed.", statusCode: 502);
        }
    }

    private static Dictionary<string, string[]> ValidateOrder(PlaceOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Items.Count == 0 || request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            errors["items"] = new[] { "At least one catalog item with a positive id and quantity is required." };
        if (request.ShippingAddress is null)
            errors["shippingAddress"] = new[] { "A shipping address is required." };
        else
        {
            var address = request.ShippingAddress;
            if (string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City) ||
                string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.ZipCode))
                errors["shippingAddress"] = new[] { "Street, city, country and zipCode are required." };
        }
        return errors;
    }

    private static OrderResponse MapOrder(Order order, IReadOnlyList<OrderNotification> notifications) => new(
        order.Id, order.OrderDate, order.Status.ToString(), order.Total(),
        order.OrderItems.Select(x => new OrderLineResponse(x.ItemOrdered.CatalogItemId,
            x.ItemOrdered.ProductName, x.Units, x.UnitPrice)).ToList(),
        notifications.Select(MapNotification).ToList());

    private static NotificationResponse MapNotification(OrderNotification notification) => new(
        notification.Id, notification.OrderId, notification.Kind.ToString(), notification.Body,
        notification.ContentDisposed, notification.ProviderMessageSid, notification.ProviderStatus,
        notification.ProviderErrorCode, notification.CreatedAt, notification.UpdatedAt,
        notification.ScheduledFor, notification.OriginalNotificationId);

    private static string BuyerId(ClaimsPrincipal user) =>
        user.Identity?.Name ?? throw new InvalidOperationException("The authenticated token has no name claim.");

    private static IResult Validation(string key, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [key] = new[] { message } });

}
