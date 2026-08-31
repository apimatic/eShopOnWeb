using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Returns a fulfilled order, fully or partially, by refunding the captured payment at
/// PayPal. The caller supplies an idempotency key: repeating the request under the same
/// key returns the original refund instead of refunding twice; distinct keys remain
/// legitimate separate partial refunds. A refund can never exceed what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ClaimsPrincipal, int>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IPaymentGateway _paymentGateway;

    public RefundOrderEndpoint(IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IPaymentGateway paymentGateway)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentGateway = paymentGateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user, orderId);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user, int orderId)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("An idempotencyKey is required for refunds.");
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null || order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId));

        var existing = payment?.Refunds.FirstOrDefault(r => r.IdempotencyKey == request.IdempotencyKey);
        if (existing != null)
        {
            // Idempotent replay under a known key: report the original refund.
            return Results.Ok(BuildResponse(request, order, payment!, existing));
        }

        if (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded)
        {
            return Results.Conflict($"Order {orderId} is {order.Status}; only a fulfilled order can be refunded.");
        }
        if (payment?.CaptureId == null)
        {
            return Results.Conflict($"Order {orderId} has no captured payment to refund.");
        }

        var amount = request.Amount ?? payment.RefundableAmount;
        if (amount <= 0 || amount > payment.RefundableAmount)
        {
            return Results.UnprocessableEntity(
                $"Refund amount {amount:0.00} {payment.Currency} exceeds the refundable remainder " +
                $"{payment.RefundableAmount:0.00} {payment.Currency} of the captured {payment.CapturedAmount:0.00} {payment.Currency}.");
        }

        PaymentRefund refund;
        try
        {
            var payPalRefund = await _paymentGateway.RefundCaptureAsync(
                payment.CaptureId, amount, payment.Currency,
                $"eshop-order-{order.Id}-refund-{request.IdempotencyKey}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
                request.NoteToPayer, $"eshop-refund-{order.Id}-{request.IdempotencyKey}");

            refund = payment.AddRefund(payPalRefund.Id, payPalRefund.Amount, payPalRefund.Status,
                request.IdempotencyKey, request.NoteToPayer);
        }
        catch (PayPalApiException ex)
        {
            return Results.UnprocessableEntity(
                $"PayPal could not refund capture {payment.CaptureId}: {ex.Message} (debug id: {ex.DebugId}). No refund was recorded.");
        }

        order.MarkRefunded(payment.RefundableAmount <= 0);
        await _orderRepository.UpdateAsync(order);
        await _paymentRepository.UpdateAsync(payment);

        return Results.Ok(BuildResponse(request, order, payment, refund));
    }

    private static RefundOrderResponse BuildResponse(RefundOrderRequest request, Order order, OrderPayment payment, PaymentRefund refund) =>
        new(request.CorrelationId())
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            OrderId = order.Id,
            OrderStatus = order.Status.ToString(),
            Amount = refund.Amount,
            Currency = payment.Currency,
            Status = refund.Status,
            TotalRefunded = payment.TotalRefunded,
            RefundableAmount = payment.RefundableAmount
        };
}
