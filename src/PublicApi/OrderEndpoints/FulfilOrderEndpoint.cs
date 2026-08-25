using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: captures the held authorization, taking the money. If the authorization
/// has gone stale it is renewed first (see IPaymentGatewayService.ReauthorizeAsync) rather than
/// failing the fulfilment outright; an authorization that can no longer be renewed surfaces as
/// a PaymentAuthorizationNotRenewableException (422) with an operator-actionable message.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest,
    (IRepository<Order> Orders, IRepository<OrderPayment> Payments, IPaymentGatewayService Gateway, CancellationToken Ct)>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orders, IRepository<OrderPayment> payments, IPaymentGatewayService gateway, CancellationToken ct) =>
            {
                return await HandleAsync(new FulfilOrderRequest(orderId), (orders, payments, gateway, ct));
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request,
        (IRepository<Order> Orders, IRepository<OrderPayment> Payments, IPaymentGatewayService Gateway, CancellationToken Ct) dependency)
    {
        var order = await dependency.Orders.GetByIdAsync(request.OrderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        var paymentSpec = new OrderPaymentByOrderIdSpec(order.Id);
        var payment = await dependency.Payments.FirstOrDefaultAsync(paymentSpec);

        if (order.Status == OrderStatus.Fulfilled)
        {
            // Idempotent-in-effect: a repeated fulfil call never captures twice.
            return Results.Ok(BuildResponse(order, payment!));
        }

        if (order.Status != OrderStatus.PaymentAuthorized || payment is null)
        {
            throw new OrderStateException($"Cannot fulfil order {order.Id} because it is in status {order.Status}; it must have an authorized payment first.");
        }

        var authorizationId = payment.AuthorizationId;

        var status = await dependency.Gateway.GetAuthorizationAsync(authorizationId, dependency.Ct);
        var isStale = status.ExpiresAt.HasValue && status.ExpiresAt.Value <= DateTimeOffset.UtcNow;

        if (isStale)
        {
            authorizationId = await RenewAsync(dependency, payment, authorizationId, "");
        }

        PaymentCaptureResult captureResult;
        var captureRequestId = $"eshop-capture-order-{order.Id}";
        try
        {
            captureResult = await dependency.Gateway.CaptureAuthorizationAsync(authorizationId, captureRequestId, dependency.Ct);
        }
        catch (PaymentGatewayException) when (!isStale)
        {
            // PayPal's capture rejection is the authoritative staleness signal (no typed status
            // for it) - fall back to one renew-and-retry even though our own check said fresh.
            authorizationId = await RenewAsync(dependency, payment, authorizationId, "-retry");
            captureResult = await dependency.Gateway.CaptureAuthorizationAsync(authorizationId, captureRequestId, dependency.Ct);
        }

        payment.RecordCapture(captureResult.CaptureId, captureResult.Status, captureResult.CapturedAmount, captureResult.FeeAmount, captureResult.NetAmount);
        await dependency.Payments.UpdateAsync(payment);

        order.MarkFulfilled();
        await dependency.Orders.UpdateAsync(order);

        return Results.Ok(BuildResponse(order, payment));
    }

    private static async Task<string> RenewAsync(
        (IRepository<Order> Orders, IRepository<OrderPayment> Payments, IPaymentGatewayService Gateway, CancellationToken Ct) dependency,
        OrderPayment payment, string authorizationId, string suffix)
    {
        var renewed = await dependency.Gateway.ReauthorizeAsync(
            authorizationId,
            new PaymentAmount(payment.Amount, payment.Currency),
            $"eshop-reauthorize-order-{payment.OrderId}-{authorizationId}{suffix}",
            dependency.Ct);

        payment.RecordReauthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
        await dependency.Payments.UpdateAsync(payment);

        return renewed.AuthorizationId;
    }

    private static FulfilOrderResponse BuildResponse(Order order, OrderPayment payment) => new()
    {
        OrderId = order.Id,
        Status = order.Status.ToString(),
        CaptureId = payment.CaptureId ?? string.Empty,
        CaptureStatus = payment.CaptureStatus ?? string.Empty,
        CapturedAmount = payment.CapturedAmount ?? 0m,
        PayPalFeeAmount = payment.PayPalFeeAmount,
        NetAmount = payment.NetAmount
    };
}
