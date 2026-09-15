using System;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var shopper = new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme };
        var administrator = new AuthorizeAttribute
        {
            AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme,
            Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS
        };

        app.MapPost("/api/orders", async (CreateOrderRequest request, HttpContext context,
                PaymentOperations operations, CancellationToken cancellationToken) =>
            {
                var response = await operations.CreateOrderAsync(GetBuyerId(context), request, cancellationToken);
                return Results.Created($"/api/orders/{response.OrderId}", response);
            })
            .RequireAuthorization(shopper)
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");

        app.MapPost("/api/orders/{orderId:int}/pay", async (int orderId, PayOrderRequest request,
                HttpContext context, PaymentOperations operations, CancellationToken cancellationToken) =>
            Results.Ok(await operations.PayAsync(GetBuyerId(context), orderId, request, cancellationToken)))
            .RequireAuthorization(shopper)
            .Produces<PayOrderResponse>()
            .WithTags("PaymentEndpoints");

        app.MapPost("/api/orders/{orderId:int}/fulfil", async (int orderId,
                PaymentOperations operations, CancellationToken cancellationToken) =>
            Results.Ok(await operations.FulfilAsync(orderId, cancellationToken)))
            .RequireAuthorization(administrator)
            .Produces<FulfilOrderResponse>()
            .WithTags("PaymentEndpoints");

        app.MapPost("/api/orders/{orderId:int}/cancel", async (int orderId,
                PaymentOperations operations, CancellationToken cancellationToken) =>
            Results.Ok(await operations.CancelAsync(orderId, cancellationToken)))
            .RequireAuthorization(administrator)
            .Produces<CancelOrderResponse>()
            .WithTags("PaymentEndpoints");

        app.MapPost("/api/orders/{orderId:int}/refunds", async (int orderId, RefundOrderRequest request,
                HttpContext context, PaymentOperations operations, CancellationToken cancellationToken) =>
            Results.Ok(await operations.RefundAsync(GetBuyerId(context), orderId, request, cancellationToken)))
            .RequireAuthorization(shopper)
            .Produces<RefundOrderResponse>()
            .WithTags("PaymentEndpoints");

        app.MapGet("/api/my-orders", async (HttpContext context, PaymentOperations operations,
                CancellationToken cancellationToken) =>
            Results.Ok(await operations.GetMyOrdersAsync(GetBuyerId(context), cancellationToken)))
            .RequireAuthorization(shopper)
            .Produces<System.Collections.Generic.IReadOnlyList<OrderResponse>>()
            .WithTags("PaymentEndpoints");

        app.MapPost("/api/payment-methods", async (SavePaymentMethodRequest request, HttpContext context,
                PaymentOperations operations, CancellationToken cancellationToken) =>
            {
                var response = await operations.SavePaymentMethodAsync(GetBuyerId(context), request, cancellationToken);
                return Results.Created($"/api/payment-methods/{response.PaymentMethodId}", response);
            })
            .RequireAuthorization(shopper)
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");

        app.MapGet("/api/payment-methods", async (HttpContext context, PaymentOperations operations,
                CancellationToken cancellationToken) =>
            Results.Ok(await operations.GetPaymentMethodsAsync(GetBuyerId(context), cancellationToken)))
            .RequireAuthorization(shopper)
            .Produces<System.Collections.Generic.IReadOnlyList<PaymentMethodResponse>>()
            .WithTags("PaymentEndpoints");

        app.MapDelete("/api/payment-methods/{paymentMethodId:int}", async (int paymentMethodId,
                HttpContext context, PaymentOperations operations, CancellationToken cancellationToken) =>
            {
                await operations.DeletePaymentMethodAsync(GetBuyerId(context), paymentMethodId, cancellationToken);
                return Results.NoContent();
            })
            .RequireAuthorization(shopper)
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentEndpoints");

        app.MapGet("/api/reconciliation", async (DateTimeOffset from, DateTimeOffset to,
                PaymentOperations operations, CancellationToken cancellationToken) =>
            Results.Ok(await operations.ReconcileAsync(from, to, cancellationToken)))
            .RequireAuthorization(administrator)
            .Produces<ReconciliationResponse>()
            .WithTags("PaymentEndpoints");

        return app;
    }

    private static string GetBuyerId(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.Name) ??
        throw new ApiProblemException(StatusCodes.Status401Unauthorized, "USER_ID_REQUIRED",
            "The bearer token does not contain a shopper identity.");
}
