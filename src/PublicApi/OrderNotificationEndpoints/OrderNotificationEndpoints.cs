using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed class OrderNotificationEndpoints : IEndpoint
{
    private const string AdminRole = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers", RegisterContactNumber)
            .RequireAuthorization(ShopperPolicy()).WithTags("ContactNumbers").Produces(StatusCodes.Status201Created);
        app.MapGet("api/contact-numbers", GetContactNumbers)
            .RequireAuthorization(ShopperPolicy()).WithTags("ContactNumbers");
        app.MapDelete("api/contact-numbers/{contactNumberId:int}", DeleteContactNumber)
            .RequireAuthorization(ShopperPolicy()).WithTags("ContactNumbers");

        app.MapPost("api/orders", PlaceOrder)
            .RequireAuthorization(ShopperPolicy()).WithTags("Orders").Produces(StatusCodes.Status201Created);
        app.MapPost("api/orders/{orderId:int}/dispatch", DispatchOrder)
            .RequireAuthorization(AdminPolicy()).WithTags("Orders");
        app.MapPost("api/orders/{orderId:int}/cancel", CancelOrder)
            .RequireAuthorization(AdminPolicy()).WithTags("Orders");
        app.MapGet("api/my-orders", GetMyOrders)
            .RequireAuthorization(ShopperPolicy()).WithTags("Orders");
        app.MapGet("api/orders/{orderId:int}/notifications", GetOrderNotifications)
            .RequireAuthorization(ShopperPolicy()).WithTags("Notifications");

        app.MapPost("api/notifications/{notificationId:int}/resend", Resend)
            .RequireAuthorization(AdminPolicy()).WithTags("Notifications");
        app.MapDelete("api/notifications/{notificationId:int}/content", DisposeContent)
            .RequireAuthorization(AdminPolicy()).WithTags("Notifications");
        app.MapGet("api/notifications/reconciliation", Reconcile)
            .RequireAuthorization(AdminPolicy()).WithTags("Notifications");
    }

    private static async Task<IResult> RegisterContactNumber(RegisterContactNumberRequest request,
        ClaimsPrincipal user, IOrderNotificationService service, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return Results.BadRequest(new { error = "phoneNumber is required." });
        try
        {
            var result = await service.RegisterContactNumberAsync(UserName(user), request.PhoneNumber,
                cancellationToken);
            return Results.Created($"/api/contact-numbers/{result.ContactNumberId}", result);
        }
        catch (InvalidContactNumberException)
        {
            return Results.BadRequest(new { error = "The messaging provider does not consider this a valid destination." });
        }
        catch (TwilioGatewayException)
        {
            return Results.Problem("The contact number could not be validated by the messaging provider.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> GetContactNumbers(ClaimsPrincipal user, IOrderNotificationService service,
        CancellationToken cancellationToken) => Results.Ok(await service.GetContactNumbersAsync(UserName(user),
        cancellationToken));

    private static async Task<IResult> DeleteContactNumber(int contactNumberId, ClaimsPrincipal user,
        IOrderNotificationService service, CancellationToken cancellationToken) =>
        await service.DeleteContactNumberAsync(UserName(user), contactNumberId, cancellationToken)
            ? Results.NoContent() : Results.NotFound();

    private static async Task<IResult> PlaceOrder(PlaceOrderRequest request, ClaimsPrincipal user,
        IOrderNotificationService service, CancellationToken cancellationToken)
    {
        if (request.ShipToAddress is null || request.Items is null || request.Items.Count == 0 ||
            string.IsNullOrWhiteSpace(request.ShipToAddress.Street) ||
            string.IsNullOrWhiteSpace(request.ShipToAddress.City) ||
            string.IsNullOrWhiteSpace(request.ShipToAddress.Country) ||
            string.IsNullOrWhiteSpace(request.ShipToAddress.ZipCode))
            return Results.BadRequest(new { error = "A shipping address and at least one order item are required." });
        var address = new ShippingAddressInput(request.ShipToAddress.Street, request.ShipToAddress.City,
            request.ShipToAddress.State ?? string.Empty, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);
        var items = request.Items.ConvertAll(x => new OrderLineInput(x.CatalogItemId, x.Quantity));
        var result = await service.PlaceOrderAsync(UserName(user), address, items, cancellationToken);
        return result is null
            ? Results.BadRequest(new { error = "Every catalog item must exist and every quantity must be positive." })
            : Results.Created($"/api/orders/{result.OrderId}", result);
    }

    private static async Task<IResult> DispatchOrder(int orderId, IOrderNotificationService service,
        CancellationToken cancellationToken) => OperationResponse(await service.DispatchOrderAsync(orderId,
        cancellationToken));

    private static async Task<IResult> CancelOrder(int orderId, IOrderNotificationService service,
        CancellationToken cancellationToken) => OperationResponse(await service.CancelOrderAsync(orderId,
        cancellationToken));

    private static async Task<IResult> GetMyOrders(ClaimsPrincipal user, IOrderNotificationService service,
        CancellationToken cancellationToken) => Results.Ok(await service.GetOrdersAsync(UserName(user),
        cancellationToken));

    private static async Task<IResult> GetOrderNotifications(int orderId, ClaimsPrincipal user,
        IOrderNotificationService service, CancellationToken cancellationToken)
    {
        var result = await service.GetOrderNotificationsAsync(UserName(user), orderId, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> Resend(int notificationId, ResendNotificationRequest request,
        IOrderNotificationService service, CancellationToken cancellationToken)
    {
        var result = await service.ResendAsync(notificationId, request.IdempotencyKey ?? string.Empty,
            cancellationToken);
        if (result.Succeeded) return Results.Created($"/api/notifications/{result.NotificationId}",
            new { notificationId = result.NotificationId });
        return result.Error == "Notification not found."
            ? Results.NotFound(new { error = result.Error })
            : Results.Conflict(new { error = result.Error });
    }

    private static async Task<IResult> DisposeContent(int notificationId, IOrderNotificationService service,
        CancellationToken cancellationToken)
    {
        var result = await service.DisposeContentAsync(notificationId, cancellationToken);
        if (result.Succeeded) return Results.NoContent();
        return result.Error == "Notification not found."
            ? Results.NotFound(new { error = result.Error })
            : Results.Problem(result.Error, statusCode: StatusCodes.Status502BadGateway);
    }

    private static async Task<IResult> Reconcile(DateTimeOffset from, DateTimeOffset to,
        IOrderNotificationService service, CancellationToken cancellationToken)
    {
        if (from > to) return Results.BadRequest(new { error = "from must be earlier than or equal to to." });
        try { return Results.Ok(await service.ReconcileAsync(from, to, cancellationToken)); }
        catch (TwilioGatewayException)
        {
            return Results.Problem("The messaging provider could not produce the reconciliation data.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static IResult OperationResponse(OperationResult result)
    {
        if (result.Succeeded) return Results.Ok();
        return result.Error == "Order not found."
            ? Results.NotFound(new { error = result.Error })
            : Results.Conflict(new { error = result.Error });
    }

    private static string UserName(ClaimsPrincipal user) => user.Identity?.Name
        ?? throw new InvalidOperationException("The authenticated token has no name claim.");
    private static AuthorizeAttribute ShopperPolicy() => new()
        { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme };
    private static AuthorizeAttribute AdminPolicy() => new()
        { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = AdminRole };
}

public sealed class RegisterContactNumberRequest
{
    public string? PhoneNumber { get; set; }
}

public sealed class PlaceOrderRequest
{
    public ShippingAddressRequest? ShipToAddress { get; set; }
    public List<PlaceOrderLineRequest>? Items { get; set; }
}

public sealed class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public sealed class PlaceOrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class ResendNotificationRequest
{
    public string? IdempotencyKey { get; set; }
}
