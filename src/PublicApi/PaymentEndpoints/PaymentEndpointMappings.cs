using System;
using System.Threading;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public static class PaymentEndpointMappings
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var shopperPolicy = new AuthorizeAttribute
        {
            AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
        };
        var shopper = app.MapGroup("/api").RequireAuthorization(shopperPolicy);

        shopper.MapPost("/orders", async (CreateOrderRequest request, HttpContext context,
            PaymentService service, CancellationToken cancellationToken) =>
            Results.Created("/api/my-orders", await service.CreateOrderAsync(UserName(context), request,
                cancellationToken)))
            .WithTags("Orders");

        shopper.MapPost("/orders/{orderId:int}/pay", async (int orderId, PayOrderRequest request,
            HttpContext context, PaymentService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.PayAsync(UserName(context), orderId, request, cancellationToken)))
            .WithTags("Orders");

        shopper.MapPost("/orders/{orderId:int}/refunds", async (int orderId, RefundOrderRequest request,
            HttpContext context, PaymentService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.RefundAsync(UserName(context), orderId, request, cancellationToken)))
            .WithTags("Orders");

        shopper.MapGet("/my-orders", async (HttpContext context, PaymentService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetOrdersAsync(UserName(context), cancellationToken)))
            .WithTags("Orders");

        shopper.MapPost("/payment-methods", async (SavePaymentMethodRequest request,
            HttpContext context, PaymentService service, CancellationToken cancellationToken) =>
            Results.Created("/api/payment-methods", await service.SavePaymentMethodAsync(
                UserName(context), request, cancellationToken)))
            .WithTags("Payment Methods");

        shopper.MapGet("/payment-methods", async (HttpContext context, PaymentService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetPaymentMethodsAsync(UserName(context), cancellationToken)))
            .WithTags("Payment Methods");

        shopper.MapDelete("/payment-methods/{paymentMethodId:int}", async (int paymentMethodId,
            HttpContext context, PaymentService service, CancellationToken cancellationToken) =>
        {
            await service.DeletePaymentMethodAsync(UserName(context), paymentMethodId, cancellationToken);
            return Results.NoContent();
        }).WithTags("Payment Methods");

        var operatorPolicy = new AuthorizeAttribute
        {
            Roles = Constants.Roles.ADMINISTRATORS,
            AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
        };
        app.MapPost("/api/orders/{orderId:int}/fulfil", async (int orderId, PaymentService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.FulfilAsync(orderId, cancellationToken)))
            .RequireAuthorization(operatorPolicy).WithTags("Order Operations");

        app.MapPost("/api/orders/{orderId:int}/cancel", async (int orderId, PaymentService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.CancelAsync(orderId, cancellationToken)))
            .RequireAuthorization(operatorPolicy).WithTags("Order Operations");

        app.MapGet("/api/reconciliation", async (DateTimeOffset from, DateTimeOffset to,
            PaymentService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ReconcileAsync(from, to, cancellationToken)))
            .RequireAuthorization(operatorPolicy).WithTags("Order Operations");

        return app;
    }

    private static string UserName(HttpContext context) => context.User.Identity?.Name ??
        throw new PaymentApiException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED",
            "The bearer token does not identify a shopper.");
}
