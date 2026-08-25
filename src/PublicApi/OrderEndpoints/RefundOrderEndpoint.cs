using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IRepository<OrderPayment>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   RefundOrderRequest request,
                   IRepository<OrderPayment> paymentRepo,
                   IPayPalClient paypal,
                   ILogger<RefundOrderEndpoint> logger) =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    return Results.BadRequest(new { error = "IdempotencyKey is required." });

                var spec = new OrderPaymentByOrderIdSpec(orderId);
                var payment = await paymentRepo.FirstOrDefaultAsync(spec);
                if (payment == null)
                    return Results.NotFound(new { error = "Order not found." });

                if (payment.Status != PaymentStatus.Captured
                    && payment.Status != PaymentStatus.PartiallyRefunded)
                    return Results.UnprocessableEntity(new { error = $"Order is in state {payment.Status}; only Captured orders can be refunded." });

                if (string.IsNullOrEmpty(payment.CaptureId))
                    return Results.UnprocessableEntity(new { error = "No capture ID on this payment." });

                // Idempotency: same key already submitted
                foreach (var existing in payment.Refunds)
                {
                    if (existing.IdempotencyKey == request.IdempotencyKey)
                        return Results.Ok(new RefundOrderResponse(existing.Id, existing.PayPalRefundId!, existing.Amount, existing.Status));
                }

                // Validate refund amount
                var capturedAmount = payment.CapturedAmount ?? payment.Amount;
                var totalRefunded = payment.TotalRefunded();
                var remaining = capturedAmount - totalRefunded;

                decimal refundAmount;
                if (request.Amount.HasValue)
                {
                    refundAmount = request.Amount.Value;
                    if (refundAmount <= 0)
                        return Results.BadRequest(new { error = "Refund amount must be positive." });
                    if (refundAmount > remaining)
                        return Results.UnprocessableEntity(new { error = $"Refund of {refundAmount} exceeds remaining refundable amount of {remaining}." });
                }
                else
                {
                    // Full remaining refund
                    refundAmount = remaining;
                    if (refundAmount <= 0)
                        return Results.UnprocessableEntity(new { error = "Nothing remaining to refund." });
                }

                try
                {
                    var ppRefund = await paypal.RefundCaptureAsync(
                        payment.CaptureId!,
                        request.Amount,
                        payment.Currency,
                        request.IdempotencyKey);

                    var refund = new PaymentRefund(request.IdempotencyKey, refundAmount, payment.Currency, ppRefund.Id, ppRefund.Status);
                    payment.AddRefund(refund);
                    await paymentRepo.UpdateAsync(payment);

                    // Re-fetch to get auto-assigned refund ID
                    var updated = await paymentRepo.FirstOrDefaultAsync(spec);
                    PaymentRefund? savedRefund = null;
                    if (updated != null)
                        foreach (var r in updated.Refunds)
                            if (r.IdempotencyKey == request.IdempotencyKey) { savedRefund = r; break; }

                    var refundId = savedRefund?.Id ?? 0;
                    return Results.Ok(new RefundOrderResponse(refundId, ppRefund.Id, refundAmount, ppRefund.Status));
                }
                catch (PayPalException ex)
                {
                    logger.LogError(ex, "PayPal refund failed for order {OrderId}", orderId);
                    return Results.UnprocessableEntity(new { error = ex.Message, detail = ex.PayPalErrorBody });
                }
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IRepository<OrderPayment> service)
        => Task.FromResult(Results.StatusCode(501));
}

public class RefundOrderRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}

public record RefundOrderResponse(int RefundId, string PayPalRefundId, decimal Amount, string Status);
