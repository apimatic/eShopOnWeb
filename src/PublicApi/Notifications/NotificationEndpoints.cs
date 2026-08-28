using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

internal static class NotificationEndpointAuthorization
{
    public const string Shopper = JwtBearerDefaults.AuthenticationScheme;
    public const string AdministratorRole = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    public static string UserId(ClaimsPrincipal principal) =>
        principal.Identity?.Name
        ?? throw new NotificationApiException(401, "An authenticated shopper identity is required.");
}

public sealed class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = NotificationEndpointAuthorization.Shopper)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal principal, OrderNotificationService service, HttpContext context) =>
            {
                var response = await service.RegisterContactNumberAsync(
                    NotificationEndpointAuthorization.UserId(principal), request, context.RequestAborted);
                return Results.Created($"/api/contact-numbers/{response.ContactNumberId}", response);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("OrderNotifications");
    }
}

public sealed class GetContactNumbersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = NotificationEndpointAuthorization.Shopper)] async
            (ClaimsPrincipal principal, OrderNotificationService service, HttpContext context) =>
                Results.Ok(await service.GetContactNumbersAsync(
                    NotificationEndpointAuthorization.UserId(principal), context.RequestAborted)))
            .WithTags("OrderNotifications");
    }
}

public sealed class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = NotificationEndpointAuthorization.Shopper)] async
            (int contactNumberId, ClaimsPrincipal principal, OrderNotificationService service, HttpContext context) =>
            {
                await service.DeleteContactNumberAsync(
                    NotificationEndpointAuthorization.UserId(principal), contactNumberId, context.RequestAborted);
                return Results.NoContent();
            })
            .WithTags("OrderNotifications");
    }
}

public sealed class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = NotificationEndpointAuthorization.Shopper)] async
            (PlaceOrderRequest request, ClaimsPrincipal principal, OrderNotificationService service, HttpContext context) =>
            {
                var response = await service.PlaceOrderAsync(
                    NotificationEndpointAuthorization.UserId(principal), request, context.RequestAborted);
                return Results.Created($"/api/orders/{response.OrderId}", response);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderNotifications");
    }
}

public sealed class DispatchOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(
                Roles = NotificationEndpointAuthorization.AdministratorRole,
                AuthenticationSchemes = NotificationEndpointAuthorization.Shopper)] async
            (int orderId, OrderNotificationService service, HttpContext context) =>
            {
                await service.DispatchOrderAsync(orderId, context.RequestAborted);
                return Results.Ok();
            })
            .WithTags("OrderNotifications");
    }
}

public sealed class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(
                Roles = NotificationEndpointAuthorization.AdministratorRole,
                AuthenticationSchemes = NotificationEndpointAuthorization.Shopper)] async
            (int orderId, OrderNotificationService service, HttpContext context) =>
            {
                await service.CancelOrderAsync(orderId, context.RequestAborted);
                return Results.Ok();
            })
            .WithTags("OrderNotifications");
    }
}

public sealed class GetMyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = NotificationEndpointAuthorization.Shopper)] async
            (ClaimsPrincipal principal, OrderNotificationService service, HttpContext context) =>
                Results.Ok(await service.GetMyOrdersAsync(
                    NotificationEndpointAuthorization.UserId(principal), context.RequestAborted)))
            .WithTags("OrderNotifications");
    }
}

public sealed class GetOrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = NotificationEndpointAuthorization.Shopper)] async
            (int orderId, ClaimsPrincipal principal, OrderNotificationService service, HttpContext context) =>
                Results.Ok(await service.GetOrderNotificationsAsync(
                    NotificationEndpointAuthorization.UserId(principal), orderId, context.RequestAborted)))
            .WithTags("OrderNotifications");
    }
}

public sealed class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(
                Roles = NotificationEndpointAuthorization.AdministratorRole,
                AuthenticationSchemes = NotificationEndpointAuthorization.Shopper)] async
            (int notificationId, ResendNotificationRequest request, OrderNotificationService service, HttpContext context) =>
                Results.Ok(await service.ResendAsync(notificationId, request.IdempotencyKey, context.RequestAborted)))
            .Produces<ResendNotificationResponse>()
            .WithTags("OrderNotifications");
    }
}

public sealed class DisposeNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(
                Roles = NotificationEndpointAuthorization.AdministratorRole,
                AuthenticationSchemes = NotificationEndpointAuthorization.Shopper)] async
            (int notificationId, OrderNotificationService service, HttpContext context) =>
            {
                await service.DisposeContentAsync(notificationId, context.RequestAborted);
                return Results.NoContent();
            })
            .WithTags("OrderNotifications");
    }
}

public sealed class ReconcileNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(
                Roles = NotificationEndpointAuthorization.AdministratorRole,
                AuthenticationSchemes = NotificationEndpointAuthorization.Shopper)] async
            (string from, string to, OrderNotificationService service, HttpContext context) =>
            {
                if (!DateTimeOffset.TryParse(from, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedFrom)
                    || !DateTimeOffset.TryParse(to, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedTo))
                {
                    throw new NotificationApiException(400, "from and to must be ISO-8601 date-times.");
                }

                return Results.Ok(await service.ReconcileAsync(parsedFrom, parsedTo, context.RequestAborted));
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderNotifications");
    }
}
