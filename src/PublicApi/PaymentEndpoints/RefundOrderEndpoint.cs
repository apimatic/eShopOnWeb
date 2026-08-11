using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Returns a captured order, in full or in part, on the caller's own order. The caller-supplied
/// idempotency key makes a repeat under the same key a no-op; the total refunded can never exceed
/// what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                RefundOrderRequest request,
                ClaimsPrincipal user,
                IPaymentService paymentService,
                CancellationToken ct) =>
            {
                var buyerId = user.BuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var refund = await paymentService.RefundAsync(
                    orderId, buyerId, request.Amount, request.IdempotencyKey, ct);

                // Re-read the payment to report the up-to-date refundable remaining.
                var response = new RefundResponse
                {
                    RefundId = refund.PayPalRefundId,
                    Amount = refund.Amount,
                    Status = refund.Status
                };
                return Results.Created($"api/orders/{orderId}/refunds/{refund.PayPalRefundId}", response);
            })
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}
