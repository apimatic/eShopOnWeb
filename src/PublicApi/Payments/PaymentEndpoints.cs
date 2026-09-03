using System;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentEndpoints : IEndpoint
{
    private const string ShopperAuth = JwtBearerDefaults.AuthenticationScheme;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
                [Authorize(AuthenticationSchemes = ShopperAuth)] async (
                    CreateOrderRequest request, ClaimsPrincipal user, PaymentApplicationService service,
                    CancellationToken ct) =>
                {
                    var result = await service.CreateOrderAsync(BuyerId(user), request, ct);
                    return Results.Created($"/api/orders/{result.OrderId}", result);
                })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");

        app.MapPost("api/orders/{orderId:int}/pay",
                [Authorize(AuthenticationSchemes = ShopperAuth)] async (
                    int orderId, PayOrderRequest request, ClaimsPrincipal user,
                    PaymentApplicationService service, CancellationToken ct) =>
                    Results.Ok(await service.PayAsync(BuyerId(user), orderId, request, ct)))
            .Produces<PaymentResponse>()
            .WithTags("PaymentEndpoints");

        app.MapPost("api/orders/{orderId:int}/fulfil",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = ShopperAuth)] async (
                    int orderId, PaymentApplicationService service, CancellationToken ct) =>
                    Results.Ok(await service.FulfilAsync(orderId, ct)))
            .Produces<PaymentResponse>()
            .WithTags("PaymentEndpoints");

        app.MapPost("api/orders/{orderId:int}/cancel",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = ShopperAuth)] async (
                    int orderId, PaymentApplicationService service, CancellationToken ct) =>
                    Results.Ok(await service.CancelAsync(orderId, ct)))
            .Produces<PaymentResponse>()
            .WithTags("PaymentEndpoints");

        app.MapPost("api/orders/{orderId:int}/refunds",
                [Authorize(AuthenticationSchemes = ShopperAuth)] async (
                    int orderId, RefundOrderRequest request, ClaimsPrincipal user,
                    PaymentApplicationService service, CancellationToken ct) =>
                    Results.Ok(await service.RefundAsync(BuyerId(user), orderId, request, ct)))
            .Produces<RefundResponse>()
            .WithTags("PaymentEndpoints");

        app.MapGet("api/my-orders",
                [Authorize(AuthenticationSchemes = ShopperAuth)] async (
                    ClaimsPrincipal user, PaymentApplicationService service, CancellationToken ct) =>
                    Results.Ok(await service.GetMyOrdersAsync(BuyerId(user), ct)))
            .Produces<OrderResponse[]>()
            .WithTags("PaymentEndpoints");

        app.MapGet("api/reconciliation",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = ShopperAuth)] async (
                    DateTimeOffset from, DateTimeOffset to, PaymentApplicationService service,
                    CancellationToken ct) => Results.Ok(await service.ReconcileAsync(from, to, ct)))
            .Produces<ReconciliationResponse>()
            .WithTags("PaymentEndpoints");

        app.MapPost("api/payment-methods",
                [Authorize(AuthenticationSchemes = ShopperAuth)] async (
                    SavePaymentMethodRequest request, ClaimsPrincipal user,
                    PaymentApplicationService service, CancellationToken ct) =>
                {
                    var result = await service.SavePaymentMethodAsync(BuyerId(user), request, ct);
                    return Results.Created($"/api/payment-methods/{result.PaymentMethodId}", result);
                })
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");

        app.MapGet("api/payment-methods",
                [Authorize(AuthenticationSchemes = ShopperAuth)] async (
                    ClaimsPrincipal user, PaymentApplicationService service, CancellationToken ct) =>
                    Results.Ok(await service.GetPaymentMethodsAsync(BuyerId(user), ct)))
            .Produces<PaymentMethodResponse[]>()
            .WithTags("PaymentEndpoints");

        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
                [Authorize(AuthenticationSchemes = ShopperAuth)] async (
                    int paymentMethodId, ClaimsPrincipal user, PaymentApplicationService service,
                    CancellationToken ct) =>
                {
                    await service.DeletePaymentMethodAsync(BuyerId(user), paymentMethodId, ct);
                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentEndpoints");
    }

    private static string BuyerId(ClaimsPrincipal user) => user.Identity?.Name ??
        throw new PaymentDomainException(StatusCodes.Status401Unauthorized, "Authenticated user identity is missing.");
}
