using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Minimal API surface for card payments, saved cards and reconciliation. Every route is
/// JWT-authenticated; operator routes require the application's existing administrator role.
/// </summary>
public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var shopperPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .Build();
        var adminPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)
            .Build();

        app.MapPost("api/orders",
                async (CreateOrderRequest request, HttpContext context, FinixPaymentService payments,
                    CancellationToken cancellationToken) =>
                {
                    var response = await payments.CreateOrderAsync(GetBuyerId(context), request, cancellationToken);
                    return Results.Created($"api/orders/{response.OrderId}", response);
                })
            .RequireAuthorization(shopperPolicy)
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("Orders");

        app.MapGet("api/my-orders",
                async (HttpContext context, FinixPaymentService payments, CancellationToken cancellationToken) =>
                    Results.Ok(await payments.GetMyOrdersAsync(GetBuyerId(context), cancellationToken)))
            .RequireAuthorization(shopperPolicy)
            .Produces<MyOrdersResponse>()
            .WithTags("Orders");

        app.MapPost("api/orders/{orderId:int}/pay",
                async (int orderId, PayOrderRequest request, HttpContext context, FinixPaymentService payments,
                    CancellationToken cancellationToken) =>
                    Results.Ok(await payments.PayOrderAsync(GetBuyerId(context), orderId, request, cancellationToken)))
            .RequireAuthorization(shopperPolicy)
            .Produces<PayOrderResponse>()
            .WithTags("Orders");

        app.MapPost("api/orders/{orderId:int}/fulfil",
                async (int orderId, FinixPaymentService payments, CancellationToken cancellationToken) =>
                    Results.Ok(await payments.FulfilOrderAsync(orderId, cancellationToken)))
            .RequireAuthorization(adminPolicy)
            .Produces<FulfilOrderResponse>()
            .WithTags("Orders");

        app.MapPost("api/orders/{orderId:int}/cancel",
                async (int orderId, FinixPaymentService payments, CancellationToken cancellationToken) =>
                    Results.Ok(await payments.CancelOrderAsync(orderId, cancellationToken)))
            .RequireAuthorization(adminPolicy)
            .Produces<CancelOrderResponse>()
            .WithTags("Orders");

        app.MapPost("api/orders/{orderId:int}/refunds",
                async (int orderId, RefundOrderRequest request, HttpContext context, FinixPaymentService payments,
                    CancellationToken cancellationToken) =>
                {
                    var isAdmin = context.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                    var response = await payments.RefundOrderAsync(GetBuyerId(context), isAdmin, orderId, request,
                        cancellationToken);
                    return Results.Created($"api/orders/{orderId}/refunds/{response.RefundId}", response);
                })
            .RequireAuthorization(shopperPolicy)
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("Orders");

        app.MapPost("api/payment-methods",
                async (CreatePaymentMethodRequest request, HttpContext context, FinixPaymentService payments,
                    CancellationToken cancellationToken) =>
                {
                    var response = await payments.CreatePaymentMethodAsync(GetBuyerId(context), request, cancellationToken);
                    return Results.Created($"api/payment-methods/{response.PaymentMethodId}", response);
                })
            .RequireAuthorization(shopperPolicy)
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethods");

        app.MapGet("api/payment-methods",
                async (HttpContext context, FinixPaymentService payments, CancellationToken cancellationToken) =>
                    Results.Ok(await payments.ListPaymentMethodsAsync(GetBuyerId(context), cancellationToken)))
            .RequireAuthorization(shopperPolicy)
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethods");

        app.MapDelete("api/payment-methods/{paymentMethodId}",
                async (string paymentMethodId, HttpContext context, FinixPaymentService payments,
                    CancellationToken cancellationToken) =>
                {
                    await payments.DeletePaymentMethodAsync(GetBuyerId(context), paymentMethodId, cancellationToken);
                    return Results.NoContent();
                })
            .RequireAuthorization(shopperPolicy)
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethods");

        app.MapGet("api/reconciliation",
                async (DateTimeOffset? from, DateTimeOffset? to, FinixPaymentService payments,
                    CancellationToken cancellationToken) =>
                {
                    if (from is null || to is null)
                    {
                        throw new PaymentException(400, "Both from and to ISO-8601 date-times are required.");
                    }

                    return Results.Ok(await payments.ReconcileAsync(from.Value, to.Value, cancellationToken));
                })
            .RequireAuthorization(adminPolicy)
            .Produces<ReconciliationResponse>()
            .WithTags("Reconciliation");

        return app;
    }

    private static string GetBuyerId(HttpContext context)
    {
        var buyerId = context.User.Identity?.Name
                      ?? context.User.FindFirst(ClaimTypes.Name)?.Value;

        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException(401, "The authenticated caller has no identity claim.");
        }

        return buyerId;
    }
}
