using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed class OrderNotificationEndpoints : IEndpoint
{
    private const string AdminRole = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers", RegisterContactNumber)
            .RequireAuthorization(ShopperPolicy()).WithTags("OrderNotifications");
        app.MapGet("api/contact-numbers", GetContactNumbers)
            .RequireAuthorization(ShopperPolicy()).WithTags("OrderNotifications");
        app.MapDelete("api/contact-numbers/{contactNumberId:int}", DeleteContactNumber)
            .RequireAuthorization(ShopperPolicy()).WithTags("OrderNotifications");

        app.MapPost("api/orders", PlaceOrder)
            .RequireAuthorization(ShopperPolicy()).WithTags("OrderNotifications");
        app.MapPost("api/orders/{orderId:int}/dispatch", DispatchOrder)
            .RequireAuthorization(OperatorPolicy()).WithTags("OrderNotifications");
        app.MapPost("api/orders/{orderId:int}/cancel", CancelOrder)
            .RequireAuthorization(OperatorPolicy()).WithTags("OrderNotifications");
        app.MapGet("api/my-orders", GetMyOrders)
            .RequireAuthorization(ShopperPolicy()).WithTags("OrderNotifications");
        app.MapGet("api/orders/{orderId:int}/notifications", GetOrderNotifications)
            .RequireAuthorization(ShopperPolicy()).WithTags("OrderNotifications");

        app.MapPost("api/notifications/{notificationId:int}/resend", Resend)
            .RequireAuthorization(OperatorPolicy()).WithTags("OrderNotifications");
        app.MapDelete("api/notifications/{notificationId:int}/content", DisposeContent)
            .RequireAuthorization(OperatorPolicy()).WithTags("OrderNotifications");
        app.MapGet("api/notifications/reconciliation", Reconciliation)
            .RequireAuthorization(OperatorPolicy()).WithTags("OrderNotifications");
    }

    private static async Task<IResult> RegisterContactNumber(RegisterContactNumberRequest request, ClaimsPrincipal user,
        IOrderNotificationService service, CancellationToken cancellationToken) =>
        await ApiResult(async () =>
        {
            var contact = await service.RegisterContactNumberAsync(BuyerId(user), request.PhoneNumber, cancellationToken);
            return Results.Created($"/api/contact-numbers/{contact.ContactNumberId}", contact);
        });

    private static async Task<IResult> GetContactNumbers(ClaimsPrincipal user, IOrderNotificationService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.GetContactNumbersAsync(BuyerId(user), cancellationToken));

    private static async Task<IResult> DeleteContactNumber(int contactNumberId, ClaimsPrincipal user,
        IOrderNotificationService service, CancellationToken cancellationToken) =>
        await ApiResult(async () => await service.DeleteContactNumberAsync(BuyerId(user), contactNumberId, cancellationToken)
            ? Results.NoContent() : Results.NotFound());

    private static async Task<IResult> PlaceOrder(PlaceOrderRequest request, ClaimsPrincipal user,
        IOrderNotificationService service, CancellationToken cancellationToken) =>
        await ApiResult(async () =>
        {
            var orderId = await service.PlaceOrderAsync(BuyerId(user), request.Items, request.ShippingAddress, cancellationToken);
            return Results.Created($"/api/orders/{orderId}", new { orderId });
        });

    private static async Task<IResult> DispatchOrder(int orderId, IOrderNotificationService service,
        CancellationToken cancellationToken) =>
        await ApiResult(async () => await service.DispatchOrderAsync(orderId, cancellationToken)
            ? Results.Ok(new { orderId, status = "dispatched" }) : Results.NotFound());

    private static async Task<IResult> CancelOrder(int orderId, IOrderNotificationService service,
        CancellationToken cancellationToken) =>
        await ApiResult(async () => await service.CancelOrderAsync(orderId, cancellationToken)
            ? Results.Ok(new { orderId, status = "cancelled" }) : Results.NotFound());

    private static async Task<IResult> GetMyOrders(ClaimsPrincipal user, IOrderNotificationService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.GetMyOrdersAsync(BuyerId(user), cancellationToken));

    private static async Task<IResult> GetOrderNotifications(int orderId, ClaimsPrincipal user,
        IOrderNotificationService service, CancellationToken cancellationToken)
    {
        var notifications = await service.GetOrderNotificationsAsync(BuyerId(user), orderId, cancellationToken);
        return notifications is null ? Results.NotFound() : Results.Ok(notifications);
    }

    private static async Task<IResult> Resend(int notificationId, ResendNotificationRequest request,
        IOrderNotificationService service, CancellationToken cancellationToken) =>
        await ApiResult(async () =>
        {
            var newId = await service.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
            return newId is null ? Results.NotFound() : Results.Ok(new { notificationId = newId.Value });
        });

    private static async Task<IResult> DisposeContent(int notificationId, IOrderNotificationService service,
        CancellationToken cancellationToken) =>
        await ApiResult(async () => await service.DisposeContentAsync(notificationId, cancellationToken)
            ? Results.NoContent() : Results.NotFound());

    private static async Task<IResult> Reconciliation(DateTimeOffset from, DateTimeOffset to,
        IOrderNotificationService service, CancellationToken cancellationToken) =>
        await ApiResult(async () => Results.Ok(await service.ReconcileAsync(from, to, cancellationToken)));

    private static string BuyerId(ClaimsPrincipal user) =>
        user.Identity?.Name ?? throw new UnauthorizedAccessException("The token does not contain a shopper identity.");

    private static AuthorizationPolicy ShopperPolicy() => new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser().Build();

    private static AuthorizationPolicy OperatorPolicy() => new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser().RequireRole(AdminRole).Build();

    private static async Task<IResult> ApiResult(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (TwilioRequestException ex) { return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway); }
    }
}

public sealed record RegisterContactNumberRequest(string PhoneNumber);
public sealed record PlaceOrderRequest(IReadOnlyList<OrderLineInput> Items, ShippingAddressInput? ShippingAddress = null);
public sealed record ResendNotificationRequest(string IdempotencyKey);
