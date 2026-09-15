using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed class OrderNotificationEndpoints : IEndpoint
{
    private const string ShopperPolicy = JwtBearerDefaults.AuthenticationScheme;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers", RegisterContactAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ShopperPolicy })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("OrderNotifications");

        app.MapGet("api/contact-numbers", GetContactsAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ShopperPolicy })
            .WithTags("OrderNotifications");

        app.MapDelete("api/contact-numbers/{contactNumberId:int}", RemoveContactAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ShopperPolicy })
            .WithTags("OrderNotifications");

        app.MapPost("api/orders", PlaceOrderAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ShopperPolicy })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderNotifications");

        app.MapPost("api/orders/{orderId:int}/dispatch", DispatchOrderAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = ShopperPolicy,
                Roles = Constants.Roles.ADMINISTRATORS
            })
            .WithTags("OrderNotifications");

        app.MapPost("api/orders/{orderId:int}/cancel", CancelOrderAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = ShopperPolicy,
                Roles = Constants.Roles.ADMINISTRATORS
            })
            .WithTags("OrderNotifications");

        app.MapGet("api/my-orders", GetMyOrdersAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ShopperPolicy })
            .WithTags("OrderNotifications");

        app.MapGet("api/orders/{orderId:int}/notifications", GetNotificationsAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ShopperPolicy })
            .WithTags("OrderNotifications");

        app.MapPost("api/notifications/{notificationId:int}/resend", ResendAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = ShopperPolicy,
                Roles = Constants.Roles.ADMINISTRATORS
            })
            .Produces<ResendNotificationResponse>(StatusCodes.Status201Created)
            .WithTags("OrderNotifications");

        app.MapDelete("api/notifications/{notificationId:int}/content", DisposeContentAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = ShopperPolicy,
                Roles = Constants.Roles.ADMINISTRATORS
            })
            .WithTags("OrderNotifications");

        app.MapGet("api/notifications/reconciliation", ReconcileAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = ShopperPolicy,
                Roles = Constants.Roles.ADMINISTRATORS
            })
            .WithTags("OrderNotifications");
    }

    private static async Task<IResult> RegisterContactAsync(
        RegisterContactNumberRequest request,
        ClaimsPrincipal user,
        IOrderNotificationService service,
        CancellationToken cancellationToken)
    {
        var contact = await service.RegisterContactAsync(BuyerId(user), request.MobileNumber, cancellationToken);
        return Results.Created(
            $"/api/contact-numbers/{contact.ContactNumberId}",
            new RegisterContactNumberResponse(contact.ContactNumberId, contact.Number));
    }

    private static async Task<IResult> GetContactsAsync(
        ClaimsPrincipal user,
        IOrderNotificationService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.GetContactsAsync(BuyerId(user), cancellationToken));

    private static async Task<IResult> RemoveContactAsync(
        int contactNumberId,
        ClaimsPrincipal user,
        IOrderNotificationService service,
        CancellationToken cancellationToken) =>
        await service.RemoveContactAsync(BuyerId(user), contactNumberId, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();

    private static async Task<IResult> PlaceOrderAsync(
        PlaceOrderRequest request,
        ClaimsPrincipal user,
        IOrderNotificationService service,
        CancellationToken cancellationToken)
    {
        var command = new PlaceOrderCommand(
            request.Items.Select(x => new PlaceOrderLine(x.CatalogItemId, x.Quantity)).ToList(),
            new ShippingAddressCommand(
                request.ShippingAddress.Street,
                request.ShippingAddress.City,
                request.ShippingAddress.State,
                request.ShippingAddress.Country,
                request.ShippingAddress.ZipCode));
        var orderId = await service.PlaceOrderAsync(BuyerId(user), command, cancellationToken);
        return Results.Created($"/api/orders/{orderId}", new PlaceOrderResponse(orderId));
    }

    private static async Task<IResult> DispatchOrderAsync(
        int orderId,
        IOrderNotificationService service,
        CancellationToken cancellationToken) =>
        await service.DispatchOrderAsync(orderId, cancellationToken)
            ? Results.Ok(new { orderId, status = "Dispatched" })
            : Results.NotFound();

    private static async Task<IResult> CancelOrderAsync(
        int orderId,
        IOrderNotificationService service,
        CancellationToken cancellationToken) =>
        await service.CancelOrderAsync(orderId, cancellationToken)
            ? Results.Ok(new { orderId, status = "Cancelled" })
            : Results.NotFound();

    private static async Task<IResult> GetMyOrdersAsync(
        ClaimsPrincipal user,
        IOrderNotificationService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.GetOrdersAsync(BuyerId(user), cancellationToken));

    private static async Task<IResult> GetNotificationsAsync(
        int orderId,
        ClaimsPrincipal user,
        IOrderNotificationService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetNotificationsAsync(BuyerId(user), orderId, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> ResendAsync(
        int notificationId,
        ResendNotificationRequest request,
        IOrderNotificationService service,
        CancellationToken cancellationToken)
    {
        var result = await service.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
        return result.HasValue
            ? Results.Created($"/api/notifications/{result.Value}", new ResendNotificationResponse(result.Value))
            : Results.NotFound();
    }

    private static async Task<IResult> DisposeContentAsync(
        int notificationId,
        IOrderNotificationService service,
        CancellationToken cancellationToken) =>
        await service.DisposeContentAsync(notificationId, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();

    private static async Task<IResult> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        IOrderNotificationService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ReconcileAsync(from, to, cancellationToken));

    private static string BuyerId(ClaimsPrincipal user) =>
        user.Identity?.Name ?? throw new UnauthorizedAccessException("The token does not contain a shopper identity.");
}
