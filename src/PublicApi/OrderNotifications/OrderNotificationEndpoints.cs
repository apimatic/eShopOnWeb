using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public sealed class OrderNotificationEndpoints : IEndpoint<IResult, CatalogContext>
{
    private const string AdminRole = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers", RegisterContactNumberAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<ContactNumberCreatedResponse>(StatusCodes.Status201Created)
            .WithTags("OrderNotifications");

        app.MapGet("api/contact-numbers", GetContactNumbersAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .WithTags("OrderNotifications");

        app.MapDelete("api/contact-numbers/{contactNumberId:int}", DeleteContactNumberAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .WithTags("OrderNotifications");

        app.MapPost("api/orders", CreateOrderAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<OrderCreatedResponse>(StatusCodes.Status201Created)
            .WithTags("OrderNotifications");

        app.MapPost("api/orders/{orderId:int}/dispatch", DispatchOrderAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme,
                Roles = AdminRole
            })
            .WithTags("OrderNotifications");

        app.MapPost("api/orders/{orderId:int}/cancel", CancelOrderAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme,
                Roles = AdminRole
            })
            .WithTags("OrderNotifications");

        app.MapGet("api/my-orders", GetMyOrdersAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .WithTags("OrderNotifications");

        app.MapGet("api/orders/{orderId:int}/notifications", GetOrderNotificationsAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .WithTags("OrderNotifications");

        app.MapPost("api/notifications/{notificationId:int}/resend", ResendNotificationAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme,
                Roles = AdminRole
            })
            .Produces<NotificationResentResponse>()
            .WithTags("OrderNotifications");

        app.MapDelete("api/notifications/{notificationId:int}/content", DisposeNotificationContentAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme,
                Roles = AdminRole
            })
            .WithTags("OrderNotifications");

        app.MapGet("api/notifications/reconciliation", ReconcileNotificationsAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme,
                Roles = AdminRole
            })
            .WithTags("OrderNotifications");
    }

    // Route handlers are split by capability above; this member satisfies the endpoint package's
    // generic contract and is not mapped directly.
    public Task<IResult> HandleAsync(CatalogContext _) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status501NotImplemented));

    private static async Task<IResult> RegisterContactNumberAsync(RegisterContactNumberRequest request,
        HttpContext httpContext, CatalogContext db, ITwilioClient twilio, CancellationToken cancellationToken)
    {
        var shopperId = ShopperId(httpContext);
        if (string.IsNullOrWhiteSpace(request.PhoneNumber) || request.PhoneNumber.Length > 64)
        {
            return Results.BadRequest(new { error = "A phoneNumber is required." });
        }

        PhoneNumberValidation validation;
        try
        {
            validation = await twilio.ValidatePhoneNumberAsync(request.PhoneNumber, cancellationToken);
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Phone number validation is temporarily unavailable.");
        }

        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            return Results.BadRequest(new { error = "The provider does not consider this a usable phone number." });
        }

        var existing = await db.ContactNumbers.SingleOrDefaultAsync(
            x => x.ShopperId == shopperId && x.PhoneNumber == validation.CanonicalNumber,
            cancellationToken);
        if (existing != null)
        {
            return Results.Conflict(new { error = "That contact number is already registered." });
        }

        var contact = new ContactNumber(shopperId, validation.CanonicalNumber);
        db.ContactNumbers.Add(contact);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/contact-numbers/{contact.Id}", new ContactNumberCreatedResponse(contact.Id));
    }

    private static async Task<IResult> GetContactNumbersAsync(HttpContext httpContext, CatalogContext db,
        CancellationToken cancellationToken)
    {
        var shopperId = ShopperId(httpContext);
        var contacts = await db.ContactNumbers.Where(x => x.ShopperId == shopperId)
            .OrderBy(x => x.Id)
            .Select(x => new ContactNumberResponse(x.Id, x.PhoneNumber, x.CreatedAt))
            .ToListAsync(cancellationToken);
        return Results.Ok(contacts);
    }

    private static async Task<IResult> DeleteContactNumberAsync(int contactNumberId, HttpContext httpContext,
        CatalogContext db, OrderNotificationService notifications, CancellationToken cancellationToken)
    {
        var shopperId = ShopperId(httpContext);
        var contact = await db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.ShopperId == shopperId, cancellationToken);
        if (contact == null)
        {
            return Results.NotFound();
        }

        if (!await notifications.CancelScheduledMessagesForContactAsync(contact.Id, cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Scheduled messages could not be cancelled; the contact number was not removed.");
        }

        db.ContactNumbers.Remove(contact);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateOrderAsync(CreateOrderRequest request, HttpContext httpContext,
        CatalogContext db, OrderNotificationService notifications, CancellationToken cancellationToken)
    {
        if (request.Items == null || request.Items.Count == 0 ||
            request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0 || x.Quantity > 100))
        {
            return Results.BadRequest(new { error = "At least one valid catalog item and quantity is required." });
        }

        if (!ValidAddress(request.ShippingAddress))
        {
            return Results.BadRequest(new { error = "A complete shippingAddress is required." });
        }

        var requested = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        if (requested.Values.Any(x => x > 100))
        {
            return Results.BadRequest(new { error = "An item quantity cannot exceed 100." });
        }

        var catalogItems = await db.CatalogItems.Where(x => requested.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != requested.Count)
        {
            return Results.BadRequest(new { error = "One or more catalog items do not exist." });
        }

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, requested[item.Id])).ToList();
        var address = request.ShippingAddress!;
        var order = new Order(ShopperId(httpContext),
            new Address(address.Street, address.City, address.State ?? string.Empty, address.Country, address.ZipCode),
            orderItems);
        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyOrderPlacedAsync(order, cancellationToken);
        return Results.Created($"/api/orders/{order.Id}", new OrderCreatedResponse(order.Id));
    }

    private static async Task<IResult> DispatchOrderAsync(int orderId, CatalogContext db,
        OrderNotificationService notifications, CancellationToken cancellationToken)
    {
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order == null)
        {
            return Results.NotFound();
        }

        try
        {
            order.Dispatch(DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }

        await db.SaveChangesAsync(cancellationToken);
        await notifications.NotifyOrderDispatchedAsync(order, cancellationToken);
        return Results.Ok(new { orderId = order.Id, status = order.Status.ToString() });
    }

    private static async Task<IResult> CancelOrderAsync(int orderId, CatalogContext db,
        OrderNotificationService notifications, CancellationToken cancellationToken)
    {
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order == null)
        {
            return Results.NotFound();
        }

        try
        {
            order.Cancel(DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }

        await db.SaveChangesAsync(cancellationToken);
        await notifications.NotifyOrderCancelledAsync(order, cancellationToken);
        return Results.Ok(new { orderId = order.Id, status = order.Status.ToString() });
    }

    private static async Task<IResult> GetMyOrdersAsync(HttpContext httpContext, CatalogContext db,
        OrderNotificationService notifications, CancellationToken cancellationToken)
    {
        var shopperId = ShopperId(httpContext);
        var orders = await db.Orders.Include(x => x.OrderItems).Where(x => x.BuyerId == shopperId)
            .OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToArray();
        var allNotifications = await db.OrderNotifications.Where(x => orderIds.Contains(x.OrderId))
            .ToListAsync(cancellationToken);
        await notifications.RefreshAsync(allNotifications, cancellationToken);

        var response = orders.Select(order => new MyOrderResponse(order.Id, order.OrderDate,
            order.Status.ToString(), order.Total(), allNotifications.Where(x => x.OrderId == order.Id)
                .OrderBy(x => x.Id).Select(ToResponse).ToList())).ToList();
        return Results.Ok(response);
    }

    private static async Task<IResult> GetOrderNotificationsAsync(int orderId, HttpContext httpContext,
        CatalogContext db, OrderNotificationService notifications, CancellationToken cancellationToken)
    {
        var shopperId = ShopperId(httpContext);
        if (!await db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == shopperId, cancellationToken))
        {
            return Results.NotFound();
        }

        var records = await db.OrderNotifications.Where(x => x.OrderId == orderId && x.ShopperId == shopperId)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        await notifications.RefreshAsync(records, cancellationToken);
        return Results.Ok(records.Select(ToResponse));
    }

    private static async Task<IResult> ResendNotificationAsync(int notificationId, ResendNotificationRequest request,
        OrderNotificationService notifications, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
        {
            return Results.BadRequest(new { error = "An idempotencyKey of at most 128 characters is required." });
        }

        var result = await notifications.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
        return result.Error switch
        {
            null => Results.Ok(new NotificationResentResponse(result.NotificationId, result.WasReplay)),
            "not_found" => Results.NotFound(),
            "not_failed" => Results.Conflict(new { error = "Only a failed or undelivered notification can be resent." }),
            "content_disposed" => Results.Conflict(new { error = "Disposed message content cannot be resent." }),
            "contact_removed" => Results.Conflict(new { error = "The destination contact number has been removed." }),
            "order_cancelled" => Results.Conflict(new { error = "A delivery follow-up cannot be resent for a cancelled order." }),
            _ => Results.Problem()
        };
    }

    private static async Task<IResult> DisposeNotificationContentAsync(int notificationId,
        OrderNotificationService notifications, CancellationToken cancellationToken)
    {
        try
        {
            return await notifications.DisposeContentAsync(notificationId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "The provider did not confirm content disposal.");
        }
    }

    private static async Task<IResult> ReconcileNotificationsAsync(DateTimeOffset? from, DateTimeOffset? to,
        OrderNotificationService notifications, CancellationToken cancellationToken)
    {
        if (!from.HasValue || !to.HasValue || from >= to)
        {
            return Results.BadRequest(new { error = "from and to must be valid ISO-8601 date-times and from must precede to." });
        }

        try
        {
            var entries = await notifications.ReconcileAsync(from.Value, to.Value, cancellationToken);
            return Results.Ok(new ReconciliationResponse(from.Value, to.Value, entries));
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "The provider reconciliation request failed.");
        }
    }

    private static NotificationResponse ToResponse(OrderNotification notification) =>
        new(notification.Id, notification.Kind.ToString(), notification.Body,
            notification.ProviderMessageSid, notification.ProviderStatus, notification.ProviderErrorCode,
            notification.ScheduledFor, notification.CreatedAt, notification.ContentDisposed,
            notification.ResendOfNotificationId);

    private static string ShopperId(HttpContext httpContext) =>
        httpContext.User.Identity?.Name ?? throw new InvalidOperationException("Authenticated user has no name claim.");

    private static bool ValidAddress(ShippingAddressRequest? address) => address != null &&
        !string.IsNullOrWhiteSpace(address.Street) && !string.IsNullOrWhiteSpace(address.City) &&
        !string.IsNullOrWhiteSpace(address.Country) && !string.IsNullOrWhiteSpace(address.ZipCode) &&
        address.Street.Length <= 180 && address.City.Length <= 100 && address.Country.Length <= 90 &&
        address.ZipCode.Length <= 18 && (address.State?.Length ?? 0) <= 60;

    private static bool IsProviderFailure(Exception exception) =>
        exception is TwilioProviderException or HttpRequestException or TaskCanceledException or
            IOException or JsonException or InvalidOperationException or FormatException;
}

public sealed record RegisterContactNumberRequest(string PhoneNumber);
public sealed record ContactNumberCreatedResponse(int ContactNumberId);
public sealed record ContactNumberResponse(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedAt);
public sealed record CreateOrderItemRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string? State, string Country, string ZipCode);
public sealed record CreateOrderRequest(IReadOnlyList<CreateOrderItemRequest> Items, ShippingAddressRequest? ShippingAddress);
public sealed record OrderCreatedResponse(int OrderId);
public sealed record NotificationResponse(int NotificationId, string Kind, string? Content,
    string? ProviderMessageSid, string ProviderStatus, int? ProviderErrorCode, DateTimeOffset? ScheduledFor,
    DateTimeOffset CreatedAt, bool ContentDisposed, int? ResendOfNotificationId);
public sealed record MyOrderResponse(int OrderId, DateTimeOffset OrderDate, string Status, decimal Total,
    IReadOnlyList<NotificationResponse> Notifications);
public sealed record ResendNotificationRequest(string IdempotencyKey);
public sealed record NotificationResentResponse(int NotificationId, bool WasReplay);
public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Entries);
