using System.Linq;
using System.Security.Claims;
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
/// Refunds a fulfilled order's captured payment, in full or in part. The caller-supplied
/// idempotency key makes a repeated request under the same key return the original refund
/// rather than refunding twice; two distinct partial refunds of the same capture remain
/// legitimate as long as their combined amount never exceeds what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest,
    (IRepository<Order> Orders, IRepository<OrderPayment> Payments, IPaymentGatewayService Gateway, ClaimsPrincipal User, CancellationToken Ct)>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IRepository<Order> orders, IRepository<OrderPayment> payments,
             IPaymentGatewayService gateway, ClaimsPrincipal user, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, (orders, payments, gateway, user, ct));
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request,
        (IRepository<Order> Orders, IRepository<OrderPayment> Payments, IPaymentGatewayService Gateway, ClaimsPrincipal User, CancellationToken Ct) dependency)
    {
        var buyerId = dependency.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await dependency.Orders.GetByIdAsync(request.OrderId);
        if (order is null || order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("idempotencyKey is required.");
        }

        if (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded)
        {
            throw new OrderStateException($"Cannot refund order {order.Id} because it is in status {order.Status}; it must be fulfilled first.");
        }

        var paymentSpec = new OrderPaymentByOrderIdSpec(order.Id);
        var payment = await dependency.Payments.FirstOrDefaultAsync(paymentSpec);
        if (payment is null || payment.CaptureId is null)
        {
            throw new OrderStateException($"Order {order.Id} has no captured payment to refund.");
        }

        var existingRefund = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == request.IdempotencyKey);
        if (existingRefund is not null)
        {
            // Idempotent replay: same key returns the original refund instead of refunding again.
            return Results.Ok(BuildResponse(order, payment, existingRefund));
        }

        var refundAmount = request.Amount ?? payment.RemainingRefundableAmount;
        if (refundAmount <= 0m)
        {
            return Results.BadRequest("There is nothing left to refund on this order.");
        }
        if (refundAmount > payment.RemainingRefundableAmount + 0.005m)
        {
            throw new RefundExceedsCapturedAmountException(refundAmount, payment.RemainingRefundableAmount);
        }

        // Always pass an explicit amount to PayPal - omitting it refunds the FULL original
        // capture, which would double-refund an order that already has a prior partial refund.
        var gatewayAmount = new PaymentAmount(refundAmount, payment.Currency);
        var refundResult = await dependency.Gateway.RefundCaptureAsync(payment.CaptureId, gatewayAmount, request.IdempotencyKey, dependency.Ct);

        var refund = payment.AddRefund(refundResult.RefundId, refundResult.Status, refundResult.Amount, request.IdempotencyKey);
        await dependency.Payments.UpdateAsync(payment);

        var isFullyRefunded = payment.RemainingRefundableAmount <= 0.005m;
        order.MarkRefunded(isPartial: !isFullyRefunded);
        await dependency.Orders.UpdateAsync(order);

        return Results.Ok(BuildResponse(order, payment, refund));
    }

    private static RefundOrderResponse BuildResponse(Order order, OrderPayment payment, ApplicationCore.Entities.OrderAggregate.PaymentRefund refund) => new()
    {
        OrderId = order.Id,
        RefundId = refund.RefundId,
        Status = refund.Status,
        Amount = refund.Amount,
        RemainingRefundableAmount = payment.RemainingRefundableAmount,
        OrderStatus = order.Status.ToString()
    };
}
