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
/// Refunds a fulfilled order, in full or in part. The caller-supplied idempotency key
/// guarantees a repeated request never refunds twice; distinct keys allow legitimate
/// multiple partial refunds, never exceeding the captured amount.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IPaymentGateway _paymentGateway;

    public RefundOrderEndpoint(IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
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
                request.OrderId = orderId;
                return await HandleAsync(request, user);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user)
    {
        var response = new RefundOrderResponse(request.CorrelationId());

        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("An idempotencyKey is required for refunds.");
        }

        var order = await _orderRepository.GetByIdAsync(request.OrderId);
        if (order is null || order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(order.Id));
        if (payment is null || payment.CaptureId is null)
        {
            return Results.Conflict($"Order {order.Id} has no captured payment to refund.");
        }

        // Idempotency: a repeated request under the same key returns the original refund.
        var existingRefund = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == request.IdempotencyKey);
        if (existingRefund is not null)
        {
            response.OrderId = order.Id;
            response.OrderStatus = order.Status.ToString();
            response.RefundId = existingRefund.PayPalRefundId;
            response.Refund = RefundDto.FromEntity(existingRefund);
            response.Payment = PaymentDto.FromEntity(payment);
            return Results.Ok(response);
        }

        if (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded)
        {
            return Results.Conflict($"Order {order.Id} is {order.Status} and cannot be refunded.");
        }

        var amount = request.Amount ?? payment.RefundableAmount;
        if (amount <= 0m || amount > payment.RefundableAmount)
        {
            return Results.UnprocessableEntity(
                $"Refund amount {amount} exceeds the refundable balance {payment.RefundableAmount} for order {order.Id}.");
        }

        GatewayRefund refund;
        try
        {
            refund = await _paymentGateway.RefundCaptureAsync(
                payment.CaptureId, amount, payment.Currency, request.IdempotencyKey, request.Note);
        }
        catch (PaymentGatewayException ex)
        {
            return Results.UnprocessableEntity(new { error = ex.Message, gatewayError = ex.GatewayErrorName });
        }

        var paymentRefund = payment.AddRefund(request.IdempotencyKey, amount, payment.Currency, request.Note);
        payment.ApplyRefund(paymentRefund, refund.RefundId, refund.Status);
        await _paymentRepository.UpdateAsync(payment);

        order.MarkRefunded(payment.RefundableAmount <= 0m);
        await _orderRepository.UpdateAsync(order);

        response.OrderId = order.Id;
        response.OrderStatus = order.Status.ToString();
        response.RefundId = refund.RefundId;
        response.Refund = RefundDto.FromEntity(paymentRefund);
        response.Payment = PaymentDto.FromEntity(payment);
        return Results.Ok(response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Partial amount to refund; omit to refund the remaining captured balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied key; repeating the request with the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public string? Note { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public string? RefundId { get; set; }
    public RefundDto? Refund { get; set; }
    public PaymentDto? Payment { get; set; }
}
