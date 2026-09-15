using System;
using System.Linq;
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
    private const string ShopperScheme = JwtBearerDefaults.AuthenticationScheme;

    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = ShopperScheme)] async (PlaceOrderRequest request,
                HttpContext context, PaymentWorkflowService service, CancellationToken cancellationToken) =>
            {
                var order = await service.PlaceOrderAsync(Identity(context), request, cancellationToken);
                return Results.Created($"/api/orders/{order.OrderId}", new CreatedOrderResponse(order.OrderId, order));
            }).Produces<CreatedOrderResponse>(StatusCodes.Status201Created).WithTags("Payments");

        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = ShopperScheme)] async (int orderId, PayOrderRequest request,
                HttpContext context, PaymentWorkflowService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.PayAsync(Identity(context), orderId, request, cancellationToken)))
            .Produces<OrderResponse>().WithTags("Payments");

        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = ShopperScheme)] async (int orderId,
                PaymentWorkflowService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.FulfilAsync(orderId, cancellationToken)))
            .Produces<OrderResponse>().WithTags("Payments");

        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = ShopperScheme)] async (int orderId,
                PaymentWorkflowService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.CancelAsync(orderId, cancellationToken)))
            .Produces<OrderResponse>().WithTags("Payments");

        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = ShopperScheme)] async (int orderId, RefundOrderRequest request,
                HttpContext context, PaymentWorkflowService service, CancellationToken cancellationToken) =>
            {
                var result = await service.RefundAsync(Identity(context), orderId, request, cancellationToken);
                var response = new CreatedRefundResponse(result.Refund.Id,
                    PaymentWorkflowService.MapRefund(result.Refund), result.Order);
                return Results.Created($"/api/orders/{orderId}/refunds/{result.Refund.Id}", response);
            }).Produces<CreatedRefundResponse>(StatusCodes.Status201Created).WithTags("Payments");

        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = ShopperScheme)] async (HttpContext context,
                PaymentWorkflowService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetOrdersAsync(Identity(context), cancellationToken)))
            .Produces<OrderResponse[]>().WithTags("Payments");

        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = ShopperScheme)] async (SavePaymentMethodRequest request,
                HttpContext context, PaymentWorkflowService service, CancellationToken cancellationToken) =>
            {
                var method = await service.SavePaymentMethodAsync(Identity(context), request, cancellationToken);
                var dto = PaymentWorkflowService.MapPaymentMethod(method);
                return Results.Created($"/api/payment-methods/{method.Id}",
                    new CreatedPaymentMethodResponse(method.Id, dto));
            }).Produces<CreatedPaymentMethodResponse>(StatusCodes.Status201Created).WithTags("Payment methods");

        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = ShopperScheme)] async (HttpContext context,
                PaymentWorkflowService service, CancellationToken cancellationToken) =>
            {
                var methods = await service.GetPaymentMethodsAsync(Identity(context), cancellationToken);
                return Results.Ok(methods.Select(PaymentWorkflowService.MapPaymentMethod));
            }).Produces<PaymentMethodResponse[]>().WithTags("Payment methods");

        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = ShopperScheme)] async (int paymentMethodId,
                HttpContext context, PaymentWorkflowService service, CancellationToken cancellationToken) =>
            {
                await service.DeletePaymentMethodAsync(Identity(context), paymentMethodId, cancellationToken);
                return Results.NoContent();
            }).WithTags("Payment methods");

        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = ShopperScheme)] async (DateTimeOffset from, DateTimeOffset to,
                PaymentWorkflowService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.ReconcileAsync(from, to, cancellationToken)))
            .Produces<ReconciliationResponse>().WithTags("Payments");

        return app;
    }

    private static string Identity(HttpContext context) => context.User.Identity?.Name ?? string.Empty;
}
