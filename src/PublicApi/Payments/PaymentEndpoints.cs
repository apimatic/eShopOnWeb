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

public static class PaymentEndpoints
{
    private const string Tag = "PaymentEndpoints";

    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, CreateOrderRequest request, PaymentService service, CancellationToken ct) =>
            {
                var result = await service.CreateOrderAsync(UserName(user), request, ct);
                return Results.Created($"/api/orders/{result.OrderId}", result);
            }).Produces<CreateOrderResponse>(StatusCodes.Status201Created).WithTags(Tag);

        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, int orderId, PayOrderRequest request, PaymentService service, CancellationToken ct) =>
                Results.Ok(await service.PayAsync(UserName(user), orderId, request, ct)))
            .Produces<OrderPaymentResponse>().WithTags(Tag);

        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PaymentService service, CancellationToken ct) =>
            {
                var result = await service.FulfilAsync(orderId, ct);
                return result.PaymentStatus == "CapturePending" ? Results.Accepted(value: result) : Results.Ok(result);
            }).Produces<OrderPaymentResponse>().Produces<OrderPaymentResponse>(StatusCodes.Status202Accepted).WithTags(Tag);

        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PaymentService service, CancellationToken ct) =>
                Results.Ok(await service.CancelAsync(orderId, ct)))
            .Produces<OrderPaymentResponse>().WithTags(Tag);

        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, int orderId, RefundOrderRequest request, PaymentService service, CancellationToken ct) =>
            {
                var result = await service.RefundAsync(UserName(user), orderId, request, ct);
                return Results.Created($"/api/orders/{orderId}/refunds/{result.RefundId}", result);
            }).Produces<PaymentRefundResponse>(StatusCodes.Status201Created).WithTags(Tag);

        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, PaymentService service, CancellationToken ct) =>
                Results.Ok(await service.MyOrdersAsync(UserName(user), ct)))
            .WithTags(Tag);

        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, PaymentService service, CancellationToken ct) =>
                Results.Ok(await service.ReconcileAsync(from, to, ct)))
            .Produces<ReconciliationResponse>().WithTags(Tag);

        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, SavePaymentMethodRequest request, PaymentService service, CancellationToken ct) =>
            {
                var result = await service.SavePaymentMethodAsync(UserName(user), request, ct);
                return Results.Created($"/api/payment-methods/{result.PaymentMethodId}", result);
            }).Produces<PaymentMethodResponse>(StatusCodes.Status201Created).WithTags(Tag);

        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, PaymentService service, CancellationToken ct) =>
                Results.Ok(await service.PaymentMethodsAsync(UserName(user), ct)))
            .WithTags(Tag);

        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, int paymentMethodId, PaymentService service, CancellationToken ct) =>
            {
                await service.DeletePaymentMethodAsync(UserName(user), paymentMethodId, ct);
                return Results.NoContent();
            }).Produces(StatusCodes.Status204NoContent).WithTags(Tag);

        return app;
    }

    private static string UserName(ClaimsPrincipal user) => user.Identity?.Name ?? string.Empty;
}
