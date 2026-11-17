using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var shopperOnly = new AuthorizeAttribute
        {
            AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
        };
        var adminOnly = new AuthorizeAttribute
        {
            Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
            AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
        };

        app.MapPost("api/orders",
                async (HttpContext http, PaymentService payments, [FromBody] CreateOrderRequest request,
                    CancellationToken ct) =>
                {
                    try
                    {
                        return Results.Ok(await payments.CreateOrderAsync(CurrentUser(http), request, ct));
                    }
                    catch (PaymentDomainException ex)
                    {
                        return PaymentResults.ProblemResult(ex);
                    }
                })
            .RequireAuthorization(shopperOnly)
            .WithTags("Orders");

        app.MapPost("api/orders/{orderId:int}/pay",
                async (int orderId, HttpContext http, PaymentService payments, [FromBody] PayOrderRequest request,
                    CancellationToken ct) =>
                {
                    try
                    {
                        return Results.Ok(await payments.PayOrderAsync(orderId, CurrentUser(http), request, ct));
                    }
                    catch (PaymentDomainException ex)
                    {
                        return PaymentResults.ProblemResult(ex);
                    }
                })
            .RequireAuthorization(shopperOnly)
            .WithTags("Orders");

        app.MapPost("api/orders/{orderId:int}/fulfil",
                async (int orderId, PaymentService payments, CancellationToken ct) =>
                {
                    try
                    {
                        return Results.Ok(await payments.FulfilOrderAsync(orderId, ct));
                    }
                    catch (PaymentDomainException ex)
                    {
                        return PaymentResults.ProblemResult(ex);
                    }
                })
            .RequireAuthorization(adminOnly)
            .WithTags("Orders");

        app.MapPost("api/orders/{orderId:int}/cancel",
                async (int orderId, PaymentService payments, CancellationToken ct) =>
                {
                    try
                    {
                        return Results.Ok(await payments.CancelOrderAsync(orderId, ct));
                    }
                    catch (PaymentDomainException ex)
                    {
                        return PaymentResults.ProblemResult(ex);
                    }
                })
            .RequireAuthorization(adminOnly)
            .WithTags("Orders");

        app.MapPost("api/orders/{orderId:int}/refunds",
                async (int orderId, HttpContext http, PaymentService payments, [FromBody] RefundOrderRequest request,
                    CancellationToken ct) =>
                {
                    try
                    {
                        return Results.Ok(await payments.RefundOrderAsync(orderId, CurrentUser(http), request, ct));
                    }
                    catch (PaymentDomainException ex)
                    {
                        return PaymentResults.ProblemResult(ex);
                    }
                })
            .RequireAuthorization(shopperOnly)
            .WithTags("Orders");

        app.MapGet("api/my-orders",
                async (HttpContext http, PaymentService payments, CancellationToken ct) =>
                    Results.Ok(await payments.GetMyOrdersAsync(CurrentUser(http), ct)))
            .RequireAuthorization(shopperOnly)
            .WithTags("MyOrders");

        app.MapGet("api/reconciliation",
                async ([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, PaymentService payments,
                    CancellationToken ct) =>
                {
                    try
                    {
                        return Results.Ok(await payments.ReconcileAsync(from, to, ct));
                    }
                    catch (PaymentDomainException ex)
                    {
                        return PaymentResults.ProblemResult(ex);
                    }
                })
            .RequireAuthorization(adminOnly)
            .WithTags("Reconciliation");

        app.MapPost("api/payment-methods",
                async (HttpContext http, PaymentService payments, [FromBody] CardRequest request,
                    CancellationToken ct) =>
                {
                    var requestKey = http.Request.Headers["Idempotency-Key"].ToString();
                    try
                    {
                        return Results.Ok(await payments.SavePaymentMethodAsync(CurrentUser(http), request,
                            string.IsNullOrWhiteSpace(requestKey) ? null : requestKey, ct));
                    }
                    catch (PaymentDomainException ex)
                    {
                        return PaymentResults.ProblemResult(ex);
                    }
                })
            .RequireAuthorization(shopperOnly)
            .WithTags("PaymentMethods");

        app.MapGet("api/payment-methods",
                async (HttpContext http, PaymentService payments, CancellationToken ct) =>
                    Results.Ok(await payments.ListPaymentMethodsAsync(CurrentUser(http), ct)))
            .RequireAuthorization(shopperOnly)
            .WithTags("PaymentMethods");

        app.MapDelete("api/payment-methods/{paymentMethodId}",
                async (string paymentMethodId, HttpContext http, PaymentService payments, CancellationToken ct) =>
                {
                    try
                    {
                        await payments.DeletePaymentMethodAsync(paymentMethodId, CurrentUser(http), ct);
                        return Results.NoContent();
                    }
                    catch (PaymentDomainException ex)
                    {
                        return PaymentResults.ProblemResult(ex);
                    }
                })
            .RequireAuthorization(shopperOnly)
            .WithTags("PaymentMethods");

        return app;
    }

    private static string CurrentUser(HttpContext http)
    {
        return http.User.Identity?.Name ?? string.Empty;
    }
}
