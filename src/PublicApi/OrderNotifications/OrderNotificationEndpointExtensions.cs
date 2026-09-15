using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public static class OrderNotificationEndpointExtensions
{
    public static IEndpointRouteBuilder MapOrderNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var shopper = app.MapGroup("/api")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
        var operators = app.MapGroup("/api")
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme,
                Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS
            });

        shopper.MapPost("/contact-numbers", (RegisterContactNumberRequest request, HttpContext context,
                OrderNotificationService service, CancellationToken ct) => ExecuteAsync(async () =>
            {
                var contact = await service.RegisterContactNumberAsync(ShopperId(context), request.Number, ct);
                return Results.Created($"/api/contact-numbers/{contact.Id}", new
                {
                    contactNumberId = contact.Id,
                    number = contact.CanonicalNumber
                });
            }))
            .WithTags("OrderNotifications")
            .Produces(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        shopper.MapGet("/contact-numbers", (HttpContext context, OrderNotificationService service, CancellationToken ct) =>
            ExecuteAsync(async () =>
            {
                var contacts = await service.GetContactNumbersAsync(ShopperId(context), ct);
                return Results.Ok(new { contactNumbers = contacts.Select(ContactNumberDto.From) });
            }))
            .WithTags("OrderNotifications");

        shopper.MapDelete("/contact-numbers/{contactNumberId:int}", (int contactNumberId, HttpContext context,
                OrderNotificationService service, CancellationToken ct) => ExecuteAsync(async () =>
            {
                await service.RemoveContactNumberAsync(ShopperId(context), contactNumberId, ct);
                return Results.NoContent();
            }))
            .WithTags("OrderNotifications");

        shopper.MapPost("/orders", (PlaceOrderRequest request, HttpContext context,
                OrderNotificationService service, CancellationToken ct) => ExecuteAsync(async () =>
            {
                var order = await service.PlaceOrderAsync(ShopperId(context), request, ct);
                return Results.Created($"/api/orders/{order.Id}", new { orderId = order.Id });
            }))
            .WithTags("OrderNotifications")
            .Produces(StatusCodes.Status201Created);

        operators.MapPost("/orders/{orderId:int}/dispatch", (int orderId,
                OrderNotificationService service, CancellationToken ct) => ExecuteAsync(async () =>
            {
                var order = await service.DispatchOrderAsync(orderId, ct);
                return Results.Ok(new { orderId = order.Id, progress = order.Progress.ToString() });
            }))
            .WithTags("OrderNotifications");

        operators.MapPost("/orders/{orderId:int}/cancel", (int orderId,
                OrderNotificationService service, CancellationToken ct) => ExecuteAsync(async () =>
            {
                var order = await service.CancelOrderAsync(orderId, ct);
                return Results.Ok(new { orderId = order.Id, progress = order.Progress.ToString() });
            }))
            .WithTags("OrderNotifications");

        shopper.MapGet("/my-orders", (HttpContext context, OrderNotificationService service, CancellationToken ct) =>
            ExecuteAsync(async () => Results.Ok(new
            {
                orders = await service.GetMyOrdersAsync(ShopperId(context), ct)
            })))
            .WithTags("OrderNotifications");

        shopper.MapGet("/orders/{orderId:int}/notifications", (int orderId, HttpContext context,
                OrderNotificationService service, CancellationToken ct) => ExecuteAsync(async () => Results.Ok(new
            {
                notifications = await service.GetOrderNotificationsAsync(ShopperId(context), orderId, ct)
            })))
            .WithTags("OrderNotifications");

        operators.MapPost("/notifications/{notificationId:int}/resend", (int notificationId,
                ResendNotificationRequest request, OrderNotificationService service, CancellationToken ct) =>
            ExecuteAsync(async () =>
            {
                var resend = await service.ResendAsync(notificationId, request.IdempotencyKey, ct);
                return Results.Ok(new { notificationId = resend.Id });
            }))
            .WithTags("OrderNotifications");

        operators.MapDelete("/notifications/{notificationId:int}/content", (int notificationId,
                OrderNotificationService service, CancellationToken ct) => ExecuteAsync(async () =>
            {
                await service.DisposeNotificationContentAsync(notificationId, ct);
                return Results.NoContent();
            }))
            .WithTags("OrderNotifications");

        operators.MapGet("/notifications/reconciliation", (DateTimeOffset from, DateTimeOffset to,
                OrderNotificationService service, CancellationToken ct) =>
            ExecuteAsync(async () => Results.Ok(await service.ReconcileAsync(from, to, ct))))
            .WithTags("OrderNotifications");

        return app;
    }

    private static string ShopperId(HttpContext context) => context.User.Identity?.Name
        ?? throw new ApiOperationException(401, "The token does not contain a shopper identity.");

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ApiOperationException ex)
        {
            return Results.Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
    }
}
