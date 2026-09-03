using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/refunds — refund the captured payment, full or partial. Carries a
/// caller-supplied idempotency key (body field or the Idempotency-Key header): repeating under the same
/// key never refunds twice. Shopper-scoped and owner-checked. Returns <c>refundId</c> as a top-level field.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                RefundOrderRequest request,
                IPaymentOrderService service,
                PayPalSettings settings,
                HttpContext http,
                System.Threading.CancellationToken ct) =>
            {
                var buyerId = http.User.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var idempotencyKey = request.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(idempotencyKey)
                    && http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
                {
                    idempotencyKey = header.ToString();
                }
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                    return Results.BadRequest("A refund requires an idempotency key (body 'idempotencyKey' or the Idempotency-Key header).");

                var (order, refund) = await service.RefundAsync(buyerId, orderId, request.Amount, idempotencyKey!, ct);

                return Results.Ok(new RefundResponse
                {
                    RefundId = refund.PayPalRefundId,
                    OrderId = order.Id,
                    PaymentStatus = order.PaymentStatus.ToString(),
                    RefundedAmount = refund.Amount,
                    TotalRefunded = order.TotalRefunded(),
                    RefundableRemaining = order.RefundableRemaining(),
                    Currency = order.Currency ?? settings.Currency
                });
            })
            .Produces<RefundResponse>()
            .WithTags("PaymentEndpoints");
    }
}
