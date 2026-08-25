using System.Linq;
using System.Security.Claims;
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
using Microsoft.eShopWeb.PublicApi.PayPal;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   RefundOrderRequest req,
                   IRepository<Order> orderRepo,
                   IPayPalService payPal,
                   IOptions<PayPalSettings> settings) =>
            {
                if (string.IsNullOrWhiteSpace(req.IdempotencyKey))
                    return Results.BadRequest(new { error = "idempotencyKey is required." });

                var order = await orderRepo.FirstOrDefaultAsync(new OrderWithPaymentSpec(orderId));
                if (order == null)
                    return Results.NotFound();

                if (order.Status != OrderStatus.Fulfilled &&
                    order.Status != OrderStatus.PartiallyRefunded)
                    return Results.BadRequest(new { error = $"Order in status {order.Status} cannot be refunded." });

                // Idempotency: if this key was already used, return the existing refund
                var existingRefund = order.Refunds.FirstOrDefault(r => r.IdempotencyKey == req.IdempotencyKey);
                if (existingRefund != null)
                    return Results.Ok(new { refundId = existingRefund.RefundId, amount = existingRefund.Amount, idempotencyKey = req.IdempotencyKey });

                // Guard partial-refund ceiling
                var alreadyRefunded = order.TotalRefunded();
                var remaining = order.CapturedAmount - alreadyRefunded;
                var refundAmount = req.Amount;

                if (refundAmount.HasValue)
                {
                    if (refundAmount.Value <= 0)
                        return Results.BadRequest(new { error = "Refund amount must be positive." });
                    if (refundAmount.Value > remaining)
                        return Results.BadRequest(new { error = $"Refund amount {refundAmount.Value} exceeds remaining refundable amount {remaining}." });
                }

                var currency = settings.Value.Currency;

                RefundResult refundResult;
                try
                {
                    refundResult = await payPal.RefundAsync(order.CaptureId!, req.IdempotencyKey, refundAmount, currency);
                }
                catch (PayPalException ex)
                {
                    return Results.BadRequest(new { error = $"Refund failed: {ex.Message}" });
                }

                var actualAmount = refundAmount ?? remaining;
                var refund = order.AddRefund(refundResult.RefundId, req.IdempotencyKey, actualAmount);
                await orderRepo.UpdateAsync(order);

                return Results.Created($"/api/orders/{orderId}/refunds/{refund.Id}",
                    new { refundId = refund.RefundId, amount = refund.Amount, idempotencyKey = refund.IdempotencyKey });
            })
            .WithTags("OrderEndpoints");
    }
}

public record RefundOrderRequest(string IdempotencyKey, decimal? Amount);
