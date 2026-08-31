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
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a fulfilled order's captured payment, in full or in part.
/// Idempotent per caller-supplied key; cumulative refunds can never exceed the captured amount.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly ILogger<RefundOrderEndpoint> _logger;

    public RefundOrderEndpoint(IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IPaymentGateway paymentGateway,
        ILogger<RefundOrderEndpoint> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "idempotencyKey is required." });
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order is null || order.BuyerId != request.BuyerId)
        {
            return Results.NotFound(new { message = $"Order {request.OrderId} not found." });
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(order.Id));

        // Idempotent replay: the same key returns the original refund.
        var existingRefund = payment?.Refunds.FirstOrDefault(r => r.IdempotencyKey == request.IdempotencyKey);
        if (existingRefund is not null)
        {
            return OkResponse(request, payment!, existingRefund);
        }

        if (order.Status != OrderStatus.Fulfilled || payment?.CaptureId is null)
        {
            return Results.Conflict(new
            {
                message = $"Order {order.Id} is {order.Status}; only a fulfilled (captured) order can be refunded."
            });
        }

        var refundable = payment.RefundableAmount();
        var amount = request.Amount ?? refundable;

        if (amount <= 0)
        {
            return Results.BadRequest(new { message = "Refund amount must be positive." });
        }

        if (amount > refundable)
        {
            return Results.Conflict(new
            {
                message = $"Cannot refund {amount:0.00} {payment.Currency}: only {refundable:0.00} {payment.Currency} " +
                    $"remains of the captured {payment.CapturedAmount:0.00}."
            });
        }

        try
        {
            var refund = await _paymentGateway.RefundCaptureAsync(payment.CaptureId, amount, payment.Currency,
                $"eshop-refund-{order.Id}-{request.IdempotencyKey}", request.NoteToPayer);

            var entity = payment.AddRefund(request.IdempotencyKey, refund.RefundId, refund.Amount, refund.Status);
            await _paymentRepository.UpdateAsync(payment);

            return OkResponse(request, payment, entity);
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Refund for order {OrderId} failed: {Error} {Issue} (debug {DebugId})",
                order.Id, ex.ErrorName, ex.Issue, ex.DebugId);
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static IResult OkResponse(RefundOrderRequest request, OrderPayment payment, PaymentRefund refund) =>
        Results.Ok(new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            OrderId = request.OrderId,
            Amount = refund.Amount,
            Status = refund.Status,
            Currency = payment.Currency,
            TotalRefunded = payment.TotalRefunded(),
            RemainingRefundable = payment.RefundableAmount()
        });
}
