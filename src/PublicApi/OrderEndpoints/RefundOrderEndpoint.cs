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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IRepository<Order>>
{
    private readonly IRepository<Payment> _paymentRepository;
    private readonly PayPalClient _payPalClient;
    private readonly PayPalSettings _payPalSettings;

    public RefundOrderEndpoint(
        IRepository<Payment> paymentRepository,
        PayPalClient payPalClient,
        Microsoft.Extensions.Options.IOptions<PayPalSettings> payPalSettings)
    {
        _paymentRepository = paymentRepository;
        _payPalClient = payPalClient;
        _payPalSettings = payPalSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, IRepository<Order> orderRepository, ClaimsPrincipal user) =>
            {
                var buyerId = user.FindFirst(ClaimTypes.Name)?.Value ?? "";
                return await HandleAsync(request with { OrderId = orderId, BuyerId = buyerId }, orderRepository);
            })
            .Produces<RefundOrderResponse>(201)
            .Produces(400)
            .Produces(404)
            .Produces(409)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IRepository<Order> orderRepository)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        if (string.IsNullOrEmpty(request.IdempotencyKey))
            return Results.BadRequest(new { error = "idempotencyKey is required." });

        var orderSpec = new OrderByIdWithItemsSpec(request.OrderId);
        var order = await orderRepository.FirstOrDefaultAsync(orderSpec);
        if (order == null || order.BuyerId != request.BuyerId)
            return Results.NotFound(new { error = "Order not found." });

        if (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded)
            return Results.BadRequest(new { error = $"Order status is {order.Status}. Only Fulfilled or PartiallyRefunded orders can be refunded." });

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(request.OrderId));
        if (payment == null || string.IsNullOrEmpty(payment.CaptureId))
            return Results.Problem("No capture record found for this order.");

        // Idempotency: check if this key was already used
        var existingRefund = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == request.IdempotencyKey);
        if (existingRefund != null)
            return Results.Created($"api/orders/{request.OrderId}/refunds/{existingRefund.Id}",
                new RefundOrderResponse { RefundId = existingRefund.Id, Amount = existingRefund.Amount, Status = "COMPLETED" });

        // Validate refund amount
        var capturedAmount = payment.CapturedAmount ?? payment.AuthorizedAmount;
        var alreadyRefunded = payment.TotalRefunded();
        var remainingRefundable = capturedAmount - alreadyRefunded;

        decimal refundAmount;
        if (request.Amount.HasValue)
        {
            if (request.Amount.Value <= 0)
                return Results.BadRequest(new { error = "Refund amount must be positive." });
            if (request.Amount.Value > remainingRefundable)
                return Results.BadRequest(new { error = $"Refund amount {request.Amount.Value} exceeds refundable amount {remainingRefundable}." });
            refundAmount = request.Amount.Value;
        }
        else
        {
            // Full refund of remaining
            refundAmount = remainingRefundable;
            if (refundAmount <= 0)
                return Results.BadRequest(new { error = "Nothing left to refund." });
        }

        try
        {
            var payPalRefund = await _payPalClient.RefundCaptureAsync(
                payment.CaptureId,
                request.Amount,
                payment.Currency,
                request.IdempotencyKey);

            // Use PayPal-reported amount if available
            if (decimal.TryParse(payPalRefund.Amount?.Value, out var actualAmount))
                refundAmount = actualAmount;

            var refund = new PaymentRefund(payment.Id, payPalRefund.Id, refundAmount, request.IdempotencyKey);
            payment.AddRefund(refund);

            await _paymentRepository.UpdateAsync(payment);

            // Determine new order status
            var newTotalRefunded = payment.TotalRefunded();
            if (newTotalRefunded >= capturedAmount)
                order.SetStatus(OrderStatus.Refunded);
            else
                order.SetStatus(OrderStatus.PartiallyRefunded);

            await orderRepository.UpdateAsync(order);

            return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}",
                new RefundOrderResponse
                {
                    RefundId = refund.Id,
                    PayPalRefundId = payPalRefund.Id,
                    Amount = refundAmount,
                    Status = payPalRefund.Status,
                    Currency = payment.Currency
                });
        }
        catch (PayPalException ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: ex.StatusCode > 0 ? ex.StatusCode : 502,
                title: "PayPalError",
                extensions: ex.DebugId != null
                    ? new System.Collections.Generic.Dictionary<string, object?> { ["debugId"] = ex.DebugId }
                    : null);
        }
    }
}
