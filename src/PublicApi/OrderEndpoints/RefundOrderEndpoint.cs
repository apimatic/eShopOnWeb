using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPalService;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = "";
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public string RefundId { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal Amount { get; set; }
}

public class RefundOrderEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, IRepository<Order> orderRepo,
                   IRepository<Payment> paymentRepo, IPayPalService paypal,
                   HttpContext httpContext, CancellationToken ct) =>
            {
                var buyerId = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                if (string.IsNullOrEmpty(request.IdempotencyKey))
                    return Results.BadRequest("IdempotencyKey is required.");

                var order = await orderRepo.FirstOrDefaultAsync(new OrderWithPaymentByBuyerSpec(orderId, buyerId), ct);
                if (order == null) return Results.NotFound("Order not found.");
                if (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded)
                    return Results.BadRequest($"Order must be Fulfilled to refund. Current: {order.Status}.");

                var payment = order.Payment;
                if (payment == null || string.IsNullOrEmpty(payment.CaptureId))
                    return Results.BadRequest("Order has no capture record.");

                // Idempotency check: same key already processed?
                // Reload payment with refunds
                var paymentWithRefunds = await paymentRepo.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);
                if (paymentWithRefunds == null) return Results.BadRequest("Payment not found.");

                var existing = paymentWithRefunds.FindRefundByKey(request.IdempotencyKey);
                if (existing != null)
                {
                    return Results.Ok(new RefundOrderResponse(request.CorrelationId())
                    {
                        RefundId = existing.PayPalRefundId,
                        Status = "Completed",
                        Amount = existing.Amount
                    });
                }

                // Guard: total refunded must not exceed captured
                var captured = payment.CapturedAmount ?? 0m;
                var alreadyRefunded = paymentWithRefunds.TotalRefunded;
                var refundAmount = request.Amount;
                var effectiveAmount = refundAmount ?? (captured - alreadyRefunded);

                if (effectiveAmount <= 0)
                    return Results.BadRequest("Nothing left to refund.");
                if (alreadyRefunded + effectiveAmount > captured)
                    return Results.BadRequest($"Refund amount {effectiveAmount:F2} would exceed captured amount {captured:F2}.");

                var result = await paypal.RefundAsync(
                    paymentWithRefunds.CaptureId!, refundAmount, paymentWithRefunds.Currency,
                    request.IdempotencyKey, ct);

                var refund = paymentWithRefunds.AddRefund(result.RefundId, request.IdempotencyKey, effectiveAmount, paymentWithRefunds.Currency);
                await paymentRepo.UpdateAsync(paymentWithRefunds, ct);

                var newTotal = paymentWithRefunds.TotalRefunded;
                if (newTotal >= captured)
                {
                    order.SetRefunded();
                    await orderRepo.UpdateAsync(order, ct);
                }
                else
                {
                    order.SetPartiallyRefunded();
                    await orderRepo.UpdateAsync(order, ct);
                }

                return Results.Created($"api/orders/{orderId}/refunds/{result.RefundId}",
                    new RefundOrderResponse(request.CorrelationId())
                    {
                        RefundId = result.RefundId,
                        Status = "Completed",
                        Amount = effectiveAmount
                    });
            })
            .Produces<RefundOrderResponse>(201)
            .Produces(400).Produces(404)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync() => Task.FromResult<IResult>(Results.StatusCode(501));
}
