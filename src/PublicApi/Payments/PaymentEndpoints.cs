using System;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var shopper = app.MapGroup("/api").RequireAuthorization(policy =>
        {
            policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
            policy.RequireAuthenticatedUser();
        });

        shopper.MapPost("/orders", async (PlaceOrderRequest request, HttpContext context,
            PaymentApplicationService service, CancellationToken ct) =>
        {
            var order = await service.PlaceOrderAsync(Buyer(context), request, ct);
            return Results.Created($"/api/orders/{order.OrderId}", new { orderId = order.OrderId, order });
        }).Produces(StatusCodes.Status201Created).WithTags("PaymentEndpoints");

        shopper.MapPost("/orders/{orderId:int}/pay", async (int orderId, PayOrderRequest request,
            HttpContext context, PaymentApplicationService service, CancellationToken ct) =>
        {
            var order = await service.PayAsync(orderId, Buyer(context), request, ct);
            return Results.Ok(new { orderId = order.OrderId, order });
        }).WithTags("PaymentEndpoints");

        shopper.MapPost("/orders/{orderId:int}/refunds", async (int orderId,
            CreateRefundRequest request, HttpContext context, PaymentApplicationService service,
            CancellationToken ct) =>
        {
            var refund = await service.RefundAsync(orderId, Buyer(context), request, ct);
            return Results.Created($"/api/orders/{orderId}", new { refundId = refund.RefundId, refund });
        }).Produces(StatusCodes.Status201Created).WithTags("PaymentEndpoints");

        shopper.MapGet("/my-orders", async (HttpContext context,
            PaymentApplicationService service, CancellationToken ct) =>
            Results.Ok(new { orders = await service.GetMyOrdersAsync(Buyer(context), ct) }))
            .WithTags("PaymentEndpoints");

        shopper.MapPost("/payment-methods", async (SavePaymentMethodRequest request,
            HttpContext context, PaymentApplicationService service, CancellationToken ct) =>
        {
            var method = await service.SavePaymentMethodAsync(Buyer(context), request, ct);
            return Results.Created($"/api/payment-methods/{method.PaymentMethodId}",
                new { paymentMethodId = method.PaymentMethodId, paymentMethod = method });
        }).Produces(StatusCodes.Status201Created).WithTags("PaymentMethodEndpoints");

        shopper.MapGet("/payment-methods", async (HttpContext context,
            PaymentApplicationService service, CancellationToken ct) =>
            Results.Ok(new { paymentMethods = await service.GetPaymentMethodsAsync(Buyer(context), ct) }))
            .WithTags("PaymentMethodEndpoints");

        shopper.MapDelete("/payment-methods/{paymentMethodId:int}", async (int paymentMethodId,
            HttpContext context, PaymentApplicationService service, CancellationToken ct) =>
        {
            await service.DeletePaymentMethodAsync(paymentMethodId, Buyer(context), ct);
            return Results.NoContent();
        }).WithTags("PaymentMethodEndpoints");

        var operators = app.MapGroup("/api").RequireAuthorization(policy =>
        {
            policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
            policy.RequireRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        });

        operators.MapPost("/orders/{orderId:int}/fulfil", async (int orderId,
            PaymentApplicationService service, CancellationToken ct) =>
        {
            var order = await service.FulfilAsync(orderId, ct);
            return order.PaymentState == "CapturePending"
                ? Results.Accepted($"/api/orders/{orderId}", new { orderId, order })
                : Results.Ok(new { orderId, order });
        }).WithTags("PaymentEndpoints");

        operators.MapPost("/orders/{orderId:int}/cancel", async (int orderId,
            PaymentApplicationService service, CancellationToken ct) =>
        {
            var order = await service.CancelAsync(orderId, ct);
            return Results.Ok(new { orderId, order });
        }).WithTags("PaymentEndpoints");

        operators.MapGet("/reconciliation", async (DateTimeOffset from, DateTimeOffset to,
            PaymentApplicationService service, CancellationToken ct) =>
            Results.Ok(await service.ReconcileAsync(from, to, ct)))
            .WithTags("PaymentEndpoints");

        return app;
    }

    private static string Buyer(HttpContext context) => context.User.Identity?.Name
        ?? throw new PaymentApiException(401, "The caller identity is missing from the token.");
}
