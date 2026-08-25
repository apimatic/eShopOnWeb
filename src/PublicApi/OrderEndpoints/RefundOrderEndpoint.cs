using System;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IRepository<Order>>
{
    private readonly IPayPalService _payPal;

    public RefundOrderEndpoint(IPayPalService payPal) => _payPal = payPal;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   RefundOrderRequest request,
                   IRepository<Order> orderRepository,
                   IRepository<OrderPayment> paymentRepository,
                   HttpContext ctx) =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    return Results.BadRequest(new { error = "idempotencyKey is required." });

                var orderSpec = new OrderWithItemsByIdSpec(orderId);
                var order = await orderRepository.FirstOrDefaultAsync(orderSpec);
                if (order == null) return Results.NotFound(new { error = "Order not found." });

                var paymentSpec = new OrderPaymentByOrderIdWithRefundsSpec(orderId);
                var payment = await paymentRepository.FirstOrDefaultAsync(paymentSpec);
                if (payment == null) return Results.NotFound(new { error = "Payment record not found." });

                if (payment.Status is not (OrderPaymentStatus.Captured
                    or OrderPaymentStatus.PartiallyRefunded))
                {
                    return Results.BadRequest(new
                    {
                        error = $"Cannot refund an order in state {payment.Status}. Order must be fulfilled first."
                    });
                }

                if (string.IsNullOrEmpty(payment.CaptureId))
                    return Results.BadRequest(new { error = "No capture ID on record." });

                // Idempotency check
                if (payment.TryGetExistingRefund(request.IdempotencyKey, out var existing) && existing != null)
                {
                    return Results.Ok(new RefundOrderResponse(request.CorrelationId())
                    {
                        RefundId = existing.PayPalRefundId,
                        Amount = existing.Amount
                    });
                }

                // Amount validation
                decimal? refundAmount = request.Amount;
                if (refundAmount.HasValue)
                {
                    if (refundAmount.Value <= 0)
                        return Results.BadRequest(new { error = "Refund amount must be positive." });
                    if (refundAmount.Value > payment.RemainingRefundable)
                        return Results.BadRequest(new
                        {
                            error = $"Refund amount {refundAmount.Value} exceeds remaining refundable {payment.RemainingRefundable}."
                        });
                }

                RefundResult refundResult;
                try
                {
                    refundResult = await _payPal.RefundAsync(
                        payment.CaptureId,
                        refundAmount,
                        request.IdempotencyKey,
                        ctx.RequestAborted);
                }
                catch (PayPalException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }

                payment.AddRefund(request.IdempotencyKey, refundResult.RefundId, refundResult.Amount);
                await paymentRepository.UpdateAsync(payment);

                var response = new RefundOrderResponse(request.CorrelationId())
                {
                    RefundId = refundResult.RefundId,
                    Amount = refundResult.Amount
                };
                return Results.Created($"api/orders/{orderId}/refunds/{refundResult.RefundId}", response);
            })
            .Produces<RefundOrderResponse>(201)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IRepository<Order> repository)
        => Task.FromResult(Results.StatusCode(501) as IResult);
}
