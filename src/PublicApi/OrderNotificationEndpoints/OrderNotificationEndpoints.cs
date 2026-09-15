using System;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed class OrderNotificationEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        var shopper = new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme };
        var administrator = new AuthorizeAttribute
        {
            AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme,
            Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS
        };

        app.MapPost("api/contact-numbers", async (RegisterContactNumberRequest request, ClaimsPrincipal user, OrderNotificationService service, CancellationToken ct) =>
                Results.Created("api/contact-numbers", await service.RegisterContactNumberAsync(Identity(user), request, ct)))
            .RequireAuthorization(shopper)
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("OrderNotifications");

        app.MapGet("api/contact-numbers", async (ClaimsPrincipal user, OrderNotificationService service, CancellationToken ct) =>
                Results.Ok(await service.GetContactNumbersAsync(Identity(user), ct)))
            .RequireAuthorization(shopper)
            .Produces<ContactNumberResponse[]>()
            .WithTags("OrderNotifications");

        app.MapDelete("api/contact-numbers/{contactNumberId:int}", async (int contactNumberId, ClaimsPrincipal user, OrderNotificationService service, CancellationToken ct) =>
            {
                await service.RemoveContactNumberAsync(Identity(user), contactNumberId, ct);
                return Results.NoContent();
            })
            .RequireAuthorization(shopper)
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("OrderNotifications");

        app.MapPost("api/orders", async (PlaceOrderRequest request, ClaimsPrincipal user, OrderNotificationService service, CancellationToken ct) =>
                Results.Created("api/my-orders", await service.PlaceOrderAsync(Identity(user), request, ct)))
            .RequireAuthorization(shopper)
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderNotifications");

        app.MapPost("api/orders/{orderId:int}/dispatch", async (int orderId, OrderNotificationService service, CancellationToken ct) =>
            {
                await service.DispatchOrderAsync(orderId, ct);
                return Results.Ok(new { orderId, status = "Dispatched" });
            })
            .RequireAuthorization(administrator)
            .Produces(StatusCodes.Status200OK)
            .WithTags("OrderNotifications");

        app.MapPost("api/orders/{orderId:int}/cancel", async (int orderId, OrderNotificationService service, CancellationToken ct) =>
            {
                await service.CancelOrderAsync(orderId, ct);
                return Results.Ok(new { orderId, status = "Cancelled" });
            })
            .RequireAuthorization(administrator)
            .Produces(StatusCodes.Status200OK)
            .WithTags("OrderNotifications");

        app.MapGet("api/my-orders", async (ClaimsPrincipal user, OrderNotificationService service, CancellationToken ct) =>
                Results.Ok(await service.GetMyOrdersAsync(Identity(user), ct)))
            .RequireAuthorization(shopper)
            .Produces<MyOrdersResponse>()
            .WithTags("OrderNotifications");

        app.MapGet("api/orders/{orderId:int}/notifications", async (int orderId, ClaimsPrincipal user, OrderNotificationService service, CancellationToken ct) =>
                Results.Ok(await service.GetOrderNotificationsAsync(Identity(user), orderId, ct)))
            .RequireAuthorization(shopper)
            .Produces<OrderNotificationsResponse>()
            .WithTags("OrderNotifications");

        app.MapPost("api/notifications/{notificationId:int}/resend", async (
                int notificationId,
                ResendNotificationRequest request,
                OrderNotificationService service,
                CancellationToken ct) =>
                Results.Created("api/notifications", await service.ResendAsync(notificationId, request, ct)))
            .RequireAuthorization(administrator)
            .Produces<ResendNotificationResponse>(StatusCodes.Status201Created)
            .WithTags("OrderNotifications");

        app.MapDelete("api/notifications/{notificationId:int}/content", async (int notificationId, OrderNotificationService service, CancellationToken ct) =>
            {
                await service.DisposeContentAsync(notificationId, ct);
                return Results.NoContent();
            })
            .RequireAuthorization(administrator)
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("OrderNotifications");

        app.MapGet("api/notifications/reconciliation", async (DateTimeOffset from, DateTimeOffset to, OrderNotificationService service, CancellationToken ct) =>
                Results.Ok(await service.ReconcileAsync(from, to, ct)))
            .RequireAuthorization(administrator)
            .Produces<ReconciliationResponse>()
            .WithTags("OrderNotifications");
    }

    private static string Identity(ClaimsPrincipal user) =>
        user.Identity?.Name ?? throw new OrderNotificationApiException(401, "The caller identity is missing.");
}
