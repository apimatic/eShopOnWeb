using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

internal static class PaymentEndpointIdentity
{
    public static string Get(HttpContext context) => context.User.Identity?.Name
        ?? throw new PaymentApiException(401, "AUTHENTICATION_REQUIRED", "A signed-in user is required.");
}

public sealed class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) => app.MapPost("api/orders",
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
        (CreateOrderRequest request, PayPalPaymentService service, HttpContext context, CancellationToken ct) =>
        {
            var response = await service.CreateOrderAsync(PaymentEndpointIdentity.Get(context), request, ct);
            return Results.Created($"/api/orders/{response.OrderId}", response);
        }).Produces<CreateOrderResponse>(StatusCodes.Status201Created).WithTags("Payments");
}

public sealed class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) => app.MapPost("api/orders/{orderId:int}/pay",
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
        (int orderId, PayOrderRequest request, PayPalPaymentService service, HttpContext context, CancellationToken ct) =>
            Results.Ok(await service.PayAsync(PaymentEndpointIdentity.Get(context), orderId, request, ct)))
        .Produces<PaymentResponse>().WithTags("Payments");
}

public sealed class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) => app.MapPost("api/orders/{orderId:int}/fulfil",
        [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
            AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
        (int orderId, PayPalPaymentService service, CancellationToken ct) =>
            Results.Ok(await service.FulfilAsync(orderId, ct)))
        .Produces<PaymentResponse>().WithTags("Payments");
}

public sealed class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) => app.MapPost("api/orders/{orderId:int}/cancel",
        [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
            AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
        (int orderId, PayPalPaymentService service, CancellationToken ct) =>
            Results.Ok(await service.CancelAsync(orderId, ct)))
        .Produces<PaymentResponse>().WithTags("Payments");
}

public sealed class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) => app.MapPost("api/orders/{orderId:int}/refunds",
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
        (int orderId, RefundOrderRequest request, PayPalPaymentService service, HttpContext context, CancellationToken ct) =>
            Results.Ok(await service.RefundAsync(PaymentEndpointIdentity.Get(context), orderId, request, ct)))
        .Produces<RefundResponse>().WithTags("Payments");
}

public sealed class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) => app.MapGet("api/my-orders",
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
        (PayPalPaymentService service, HttpContext context, CancellationToken ct) =>
            Results.Ok(await service.MyOrdersAsync(PaymentEndpointIdentity.Get(context), ct)))
        .WithTags("Payments");
}

public sealed class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) => app.MapPost("api/payment-methods",
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
        (SavePaymentMethodRequest request, PayPalPaymentService service, HttpContext context, CancellationToken ct) =>
        {
            var response = await service.SavePaymentMethodAsync(PaymentEndpointIdentity.Get(context), request, ct);
            return Results.Created($"/api/payment-methods/{response.PaymentMethodId}", response);
        }).Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created).WithTags("PaymentMethods");
}

public sealed class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) => app.MapGet("api/payment-methods",
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
        (PayPalPaymentService service, HttpContext context, CancellationToken ct) =>
            Results.Ok(await service.ListPaymentMethodsAsync(PaymentEndpointIdentity.Get(context), ct)))
        .WithTags("PaymentMethods");
}

public sealed class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) => app.MapDelete("api/payment-methods/{paymentMethodId:int}",
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
        (int paymentMethodId, PayPalPaymentService service, HttpContext context, CancellationToken ct) =>
        {
            await service.DeletePaymentMethodAsync(PaymentEndpointIdentity.Get(context), paymentMethodId, ct);
            return Results.NoContent();
        }).WithTags("PaymentMethods");
}

public sealed class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) => app.MapGet("api/reconciliation",
        [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
            AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
        (DateTimeOffset from, DateTimeOffset to, PayPalPaymentService service, CancellationToken ct) =>
            Results.Ok(await service.ReconcileAsync(from, to, ct)))
        .Produces<ReconciliationResponse>().WithTags("Payments");
}
