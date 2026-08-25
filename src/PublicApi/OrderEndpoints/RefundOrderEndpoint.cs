using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, IRepository<Order> orderRepo, IRepository<PaymentInfo> paymentRepo, IRepository<RefundRecord> refundRepo, IPayPalService payPal, ClaimsPrincipal user) =>
            {
                var buyerId = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                if (string.IsNullOrEmpty(request.IdempotencyKey))
                    return Results.BadRequest("IdempotencyKey is required.");

                var order = await orderRepo.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId));
                if (order == null) return Results.NotFound();
                if (order.BuyerId != buyerId && !user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS))
                    return Results.Forbid();

                var payment = order.Payment;
                if (payment?.CaptureId == null)
                    return Results.Conflict("No capture found for this order.");

                // Idempotency check: return existing refund if same key was already processed
                var existingRefund = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == request.IdempotencyKey);

                if (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded && existingRefund == null)
                    return Results.Conflict($"Order must be Fulfilled to refund. Current status: {order.Status}.");
                if (existingRefund != null)
                {
                    return Results.Ok(new RefundOrderResponse
                    {
                        RefundId = existingRefund.RefundId,
                        Amount = existingRefund.Amount,
                        TotalRefunded = payment.TotalRefunded
                    });
                }

                // Over-refund prevention
                if (request.Amount.HasValue)
                {
                    var remaining = payment.CapturedAmount - payment.TotalRefunded;
                    if (request.Amount.Value > remaining)
                        return Results.UnprocessableEntity($"Refund amount {request.Amount:F2} exceeds remaining refundable amount {remaining:F2}.");
                }

                var refundResult = await payPal.RefundPaymentAsync(
                    payment.CaptureId,
                    request.Amount,
                    payment.Currency,
                    request.IdempotencyKey);

                var refundRecord = new RefundRecord(payment.Id, refundResult.RefundId, request.IdempotencyKey, refundResult.Amount, "COMPLETED");
                payment.AddRefund(refundRecord);
                await paymentRepo.UpdateAsync(payment);

                if (payment.TotalRefunded >= payment.CapturedAmount)
                {
                    order.UpdateStatus(OrderStatus.Refunded);
                    await orderRepo.UpdateAsync(order);
                }
                else if (payment.TotalRefunded > 0)
                {
                    order.UpdateStatus(OrderStatus.PartiallyRefunded);
                    await orderRepo.UpdateAsync(order);
                }

                return Results.Ok(new RefundOrderResponse
                {
                    RefundId = refundResult.RefundId,
                    Amount = refundResult.Amount,
                    Currency = refundResult.Currency,
                    TotalRefunded = refundResult.TotalRefunded
                });
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}
