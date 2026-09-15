using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var shopper = new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme };
        var admin = new AuthorizeAttribute
        {
            AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme,
            Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS
        };
        var api = app.MapGroup("/api").RequireAuthorization(shopper).WithTags("Payments");

        api.MapPost("/orders", async (PlaceOrderRequest request, PaymentService service, HttpContext context,
                CancellationToken ct) =>
            Results.Created("/api/my-orders", await service.PlaceOrderAsync(User(context), request, ct)))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created);

        api.MapPost("/orders/{orderId:int}/pay", async (int orderId, PayOrderRequest request,
                PaymentService service, HttpContext context, CancellationToken ct) =>
            Results.Ok(await service.PayAsync(User(context), orderId, request, ct)))
            .Produces<PaymentResponse>();

        api.MapPost("/orders/{orderId:int}/fulfil", async (int orderId, PaymentService service,
                CancellationToken ct) => Results.Ok(await service.FulfilAsync(orderId, ct)))
            .RequireAuthorization(admin).Produces<FulfilResponse>();

        api.MapPost("/orders/{orderId:int}/cancel", async (int orderId, PaymentService service,
                CancellationToken ct) => Results.Ok(await service.CancelAsync(orderId, ct)))
            .RequireAuthorization(admin).Produces<CancelResponse>();

        api.MapPost("/orders/{orderId:int}/refunds", async (int orderId, RefundOrderRequest request,
                PaymentService service, HttpContext context, CancellationToken ct) =>
            Results.Ok(await service.RefundAsync(User(context), orderId, request, ct)))
            .Produces<RefundResponse>();

        api.MapGet("/my-orders", async (PaymentService service, HttpContext context, CancellationToken ct) =>
            Results.Ok(await service.MyOrdersAsync(User(context), ct)));

        api.MapPost("/payment-methods", async (SavePaymentMethodRequest request, PaymentService service,
                HttpContext context, CancellationToken ct) =>
            Results.Created("/api/payment-methods", await service.SaveMethodAsync(User(context), request, ct)))
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created);

        api.MapGet("/payment-methods", async (PaymentService service, HttpContext context, CancellationToken ct) =>
            Results.Ok(await service.MethodsAsync(User(context), ct)));

        api.MapDelete("/payment-methods/{paymentMethodId:int}", async (int paymentMethodId,
                PaymentService service, HttpContext context, CancellationToken ct) =>
            {
                await service.DeleteMethodAsync(User(context), paymentMethodId, ct);
                return Results.NoContent();
            });

        api.MapGet("/reconciliation", async (DateTimeOffset from, DateTimeOffset to,
                PaymentService service, CancellationToken ct) =>
            Results.Ok(await service.ReconcileAsync(from, to, ct)))
            .RequireAuthorization(admin).Produces<ReconciliationResponse>();

        return app;
    }

    private static string User(HttpContext context) => context.User.Identity?.Name
        ?? throw new PaymentOperationException(401, "The access token has no user identity.");
}
