using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public sealed class OrderNotificationEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers", RegisterContactNumber)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderNotificationEndpoints");

        app.MapGet("api/contact-numbers", GetContactNumbers)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .WithTags("OrderNotificationEndpoints");

        app.MapDelete("api/contact-numbers/{contactNumberId:int}", DeleteContactNumber)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .WithTags("OrderNotificationEndpoints");

        app.MapPost("api/orders", PlaceOrder)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderNotificationEndpoints");

        app.MapPost("api/orders/{orderId:int}/dispatch", DispatchOrder)
            .RequireAuthorization(new AuthorizeAttribute
            {
                Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            }).WithTags("OrderNotificationEndpoints");

        app.MapPost("api/orders/{orderId:int}/cancel", CancelOrder)
            .RequireAuthorization(new AuthorizeAttribute
            {
                Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            }).WithTags("OrderNotificationEndpoints");

        app.MapGet("api/my-orders", GetMyOrders)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .WithTags("OrderNotificationEndpoints");

        app.MapGet("api/orders/{orderId:int}/notifications", GetOrderNotifications)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .WithTags("OrderNotificationEndpoints");

        app.MapPost("api/notifications/{notificationId:int}/resend", Resend)
            .RequireAuthorization(new AuthorizeAttribute
            {
                Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            }).WithTags("OrderNotificationEndpoints");

        app.MapDelete("api/notifications/{notificationId:int}/content", DisposeContent)
            .RequireAuthorization(new AuthorizeAttribute
            {
                Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            }).WithTags("OrderNotificationEndpoints");

        app.MapGet("api/notifications/reconciliation", Reconcile)
            .RequireAuthorization(new AuthorizeAttribute
            {
                Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            }).WithTags("OrderNotificationEndpoints");
    }

    private static async Task<IResult> RegisterContactNumber(ContactNumberRequest request, HttpContext context,
        IOrderNotificationService service, CancellationToken ct)
    {
        try
        {
            var id = await service.RegisterContactNumberAsync(Shopper(context), request.Number, ct);
            return Results.Created($"/api/contact-numbers/{id}", new { contactNumberId = id });
        }
        catch (ContactNumberValidationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (TwilioProviderException) { return Results.Problem("The phone-number provider is unavailable.", statusCode: 502); }
    }

    private static async Task<IResult> GetContactNumbers(HttpContext context,
        IOrderNotificationService service, CancellationToken ct) =>
        Results.Ok(await service.GetContactNumbersAsync(Shopper(context), ct));

    private static async Task<IResult> DeleteContactNumber(int contactNumberId, HttpContext context,
        IOrderNotificationService service, CancellationToken ct) =>
        await service.DeleteContactNumberAsync(Shopper(context), contactNumberId, ct)
            ? Results.NoContent() : Results.NotFound();

    private static async Task<IResult> PlaceOrder(PlaceOrderRequest request, HttpContext context,
        IOrderNotificationService service, CancellationToken ct)
    {
        try
        {
            var address = new Address(request.ShippingAddress.Street, request.ShippingAddress.City,
                request.ShippingAddress.State, request.ShippingAddress.Country, request.ShippingAddress.ZipCode);
            var lines = request.Items.ConvertAll(x => new OrderLineRequest(x.CatalogItemId, x.Quantity));
            var id = await service.PlaceOrderAsync(Shopper(context), address, lines, ct);
            return Results.Created($"/api/orders/{id}", new { orderId = id });
        }
        catch (NotificationOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
    }

    private static async Task<IResult> DispatchOrder(int orderId, IOrderNotificationService service, CancellationToken ct)
    {
        try { return await service.DispatchOrderAsync(orderId, ct) ? Results.Ok(new { orderId, status = "Dispatched" }) : Results.NotFound(); }
        catch (NotificationOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
    }

    private static async Task<IResult> CancelOrder(int orderId, IOrderNotificationService service, CancellationToken ct)
    {
        try { return await service.CancelOrderAsync(orderId, ct) ? Results.Ok(new { orderId, status = "Cancelled" }) : Results.NotFound(); }
        catch (NotificationOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
    }

    private static async Task<IResult> GetMyOrders(HttpContext context,
        IOrderNotificationService service, CancellationToken ct) =>
        Results.Ok(await service.GetMyOrdersAsync(Shopper(context), ct));

    private static async Task<IResult> GetOrderNotifications(int orderId, HttpContext context,
        IOrderNotificationService service, CancellationToken ct)
    {
        var result = await service.GetOrderNotificationsAsync(Shopper(context), orderId, ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> Resend(int notificationId, ResendRequest request,
        IOrderNotificationService service, CancellationToken ct)
    {
        try
        {
            var id = await service.ResendAsync(notificationId, request.IdempotencyKey, ct);
            return id is null ? Results.NotFound() : Results.Ok(new { notificationId = id.Value });
        }
        catch (NotificationOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
    }

    private static async Task<IResult> DisposeContent(int notificationId,
        IOrderNotificationService service, CancellationToken ct)
    {
        try { return await service.DisposeContentAsync(notificationId, ct) ? Results.NoContent() : Results.NotFound(); }
        catch (NotificationOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (TwilioProviderException) { return Results.Problem("The messaging provider is unavailable.", statusCode: 502); }
    }

    private static async Task<IResult> Reconcile(DateTimeOffset from, DateTimeOffset to,
        IOrderNotificationService service, CancellationToken ct)
    {
        try { return Results.Ok(await service.ReconcileAsync(from, to, ct)); }
        catch (NotificationOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (TwilioProviderException) { return Results.Problem("The messaging provider is unavailable.", statusCode: 502); }
    }

    private static string Shopper(HttpContext context) => context.User.Identity?.Name
        ?? throw new InvalidOperationException("The authenticated token has no shopper identity.");
}

public sealed record ContactNumberRequest(string Number);
public sealed record PlaceOrderItemRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);
public sealed record PlaceOrderRequest(List<PlaceOrderItemRequest> Items, ShippingAddressRequest ShippingAddress);
public sealed record ResendRequest(string IdempotencyKey);
