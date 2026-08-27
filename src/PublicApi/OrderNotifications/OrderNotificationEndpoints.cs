using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public sealed class ContactNumberEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    RegisterContactNumberRequest request,
                    HttpContext context,
                    OrderNotificationCoordinator coordinator,
                    CancellationToken cancellationToken) =>
                    await EndpointExecution.RunAsync(async () =>
                    {
                        var response = await coordinator.RegisterContactNumberAsync(BuyerId(context), request, cancellationToken);
                        return Results.Created($"/api/contact-numbers/{response.ContactNumberId}", response);
                    }))
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumbers");

        app.MapGet("api/contact-numbers",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    HttpContext context,
                    OrderNotificationCoordinator coordinator,
                    CancellationToken cancellationToken) =>
                    await EndpointExecution.RunAsync(async () =>
                        Results.Ok(await coordinator.ListContactNumbersAsync(BuyerId(context), cancellationToken))))
            .Produces<ContactNumberListResponse>()
            .WithTags("ContactNumbers");

        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    int contactNumberId,
                    HttpContext context,
                    OrderNotificationCoordinator coordinator,
                    CancellationToken cancellationToken) =>
                    await EndpointExecution.RunAsync(async () =>
                    {
                        await coordinator.DeleteContactNumberAsync(BuyerId(context), contactNumberId, cancellationToken);
                        return Results.NoContent();
                    }))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("ContactNumbers");
    }

    internal static string BuyerId(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.Name) ??
        throw new OrderNotificationApiException(401, "A signed-in shopper is required.");
}

public sealed class OrderNotificationOrderEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    PlaceOrderRequest request,
                    HttpContext context,
                    OrderNotificationCoordinator coordinator,
                    CancellationToken cancellationToken) =>
                    await EndpointExecution.RunAsync(async () =>
                    {
                        var response = await coordinator.PlaceOrderAsync(ContactNumberEndpoints.BuyerId(context), request, cancellationToken);
                        return Results.Created($"/api/orders/{response.OrderId}", response);
                    }))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("Orders");

        app.MapPost("api/orders/{orderId:int}/dispatch",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    int orderId,
                    OrderNotificationCoordinator coordinator,
                    CancellationToken cancellationToken) =>
                    await EndpointExecution.RunAsync(async () =>
                    {
                        await coordinator.DispatchOrderAsync(orderId, cancellationToken);
                        return Results.Ok(new { orderId, status = "Dispatched" });
                    }))
            .WithTags("Orders");

        app.MapPost("api/orders/{orderId:int}/cancel",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    int orderId,
                    OrderNotificationCoordinator coordinator,
                    CancellationToken cancellationToken) =>
                    await EndpointExecution.RunAsync(async () =>
                    {
                        await coordinator.CancelOrderAsync(orderId, cancellationToken);
                        return Results.Ok(new { orderId, status = "Cancelled" });
                    }))
            .WithTags("Orders");

        app.MapGet("api/my-orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    HttpContext context,
                    OrderNotificationCoordinator coordinator,
                    CancellationToken cancellationToken) =>
                    await EndpointExecution.RunAsync(async () =>
                        Results.Ok(await coordinator.GetMyOrdersAsync(ContactNumberEndpoints.BuyerId(context), cancellationToken))))
            .Produces<MyOrdersResponse>()
            .WithTags("Orders");

        app.MapGet("api/orders/{orderId:int}/notifications",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    int orderId,
                    HttpContext context,
                    OrderNotificationCoordinator coordinator,
                    CancellationToken cancellationToken) =>
                    await EndpointExecution.RunAsync(async () =>
                        Results.Ok(await coordinator.GetOrderNotificationsAsync(ContactNumberEndpoints.BuyerId(context), orderId, cancellationToken))))
            .Produces<OrderNotificationsResponse>()
            .WithTags("Orders");
    }
}

public sealed class NotificationOperatorEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    int notificationId,
                    ResendNotificationRequest request,
                    OrderNotificationCoordinator coordinator,
                    CancellationToken cancellationToken) =>
                    await EndpointExecution.RunAsync(async () =>
                        Results.Ok(await coordinator.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken))))
            .Produces<ResendNotificationResponse>()
            .WithTags("Notifications");

        app.MapDelete("api/notifications/{notificationId:int}/content",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    int notificationId,
                    OrderNotificationCoordinator coordinator,
                    CancellationToken cancellationToken) =>
                    await EndpointExecution.RunAsync(async () =>
                        Results.Ok(await coordinator.DisposeContentAsync(notificationId, cancellationToken))))
            .Produces<ContentDisposalResponse>()
            .WithTags("Notifications");

        app.MapGet("api/notifications/reconciliation",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    DateTimeOffset from,
                    DateTimeOffset to,
                    OrderNotificationCoordinator coordinator,
                    CancellationToken cancellationToken) =>
                    await EndpointExecution.RunAsync(async () =>
                        Results.Ok(await coordinator.ReconcileAsync(from, to, cancellationToken))))
            .Produces<ReconciliationResponse>()
            .WithTags("Notifications");
    }
}

internal static class EndpointExecution
{
    public static async Task<IResult> RunAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (OrderNotificationApiException ex)
        {
            return Results.Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
    }
}
