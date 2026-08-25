using System.Linq;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest
{
    public string IdempotencyKey { get; set; } = "";
    public decimal? Amount { get; set; }
}

public class RefundOrderResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = "";
    public decimal Amount { get; set; }
    public string PaymentStatus { get; set; } = "";
}

public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, HttpContext ctx,
                   IRepository<Order> orderRepo,
                   PayPalClient paypal,
                   IOptions<PayPalSettings> settings) =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    return Results.BadRequest(new { error = "idempotencyKey is required." });

                var spec = new OrderWithPaymentSpec(orderId);
                var order = await orderRepo.GetBySpecAsync(spec);
                if (order == null)
                    return Results.NotFound(new { error = "Order not found." });

                if (order.PaymentStatus != PaymentStatus.Fulfilled &&
                    order.PaymentStatus != PaymentStatus.PartiallyRefunded)
                {
                    return Results.UnprocessableEntity(new
                    {
                        error = $"Order cannot be refunded in its current state: {order.PaymentStatus}. " +
                                "Only fulfilled or partially-refunded orders can be refunded."
                    });
                }

                // Idempotency: same key → return existing refund
                var existing = order.Refunds.FirstOrDefault(r => r.IdempotencyKey == request.IdempotencyKey);
                if (existing != null)
                {
                    return Results.Ok(new RefundOrderResponse
                    {
                        RefundId = existing.Id,
                        PayPalRefundId = existing.PayPalRefundId,
                        Amount = existing.Amount,
                        PaymentStatus = order.PaymentStatus.ToString()
                    });
                }

                var currency = settings.Value.Currency;
                var remaining = order.CapturedAmount - order.RefundedAmount;

                if (request.Amount.HasValue)
                {
                    if (request.Amount.Value <= 0)
                        return Results.BadRequest(new { error = "Refund amount must be positive." });
                    if (request.Amount.Value > remaining)
                        return Results.UnprocessableEntity(new
                        {
                            error = $"Refund amount {request.Amount.Value:F2} exceeds remaining refundable amount {remaining:F2}."
                        });
                }

                // Namespace the caller's key with the capture ID so the same caller key
                // on a different capture (e.g. after app restart) doesn't collide at PayPal.
                var paypalIdempotencyKey = $"{order.PayPalCaptureId}-{request.IdempotencyKey}";
                if (paypalIdempotencyKey.Length > 38)
                    paypalIdempotencyKey = paypalIdempotencyKey.Substring(0, 38);

                PayPalRefundResult refundResult;
                try
                {
                    refundResult = await paypal.RefundCaptureAsync(
                        order.PayPalCaptureId!,
                        request.Amount,
                        currency,
                        paypalIdempotencyKey);
                }
                catch (PayPalException ex)
                {
                    return Results.UnprocessableEntity(new
                    {
                        error = $"Refund failed: {ex.Message}",
                        paypalCode = ex.PayPalName
                    });
                }

                var refundAmount = request.Amount ?? remaining;
                var refund = order.AddRefund(request.IdempotencyKey, refundResult.RefundId, refundAmount);
                await orderRepo.UpdateAsync(order);

                return Results.Created($"/api/orders/{orderId}/refunds/{refund.Id}", new RefundOrderResponse
                {
                    RefundId = refund.Id,
                    PayPalRefundId = refundResult.RefundId,
                    Amount = refundAmount,
                    PaymentStatus = order.PaymentStatus.ToString()
                });
            })
            .Produces<RefundOrderResponse>(201)
            .ProducesProblem(400)
            .ProducesProblem(422)
            .WithTags("OrderEndpoints");
    }
}
