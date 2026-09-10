using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/refunds — returns a captured payment, in full or in part. Carries a
/// caller-supplied idempotency key: repeating a request under the same key never refunds twice, while
/// two distinct partial refunds remain legitimate. Returns the refund's id as a top-level field.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                RefundRequest request,
                ClaimsPrincipal user,
                IPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(user);

                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    throw new PaymentDomainException("A refund requires a non-empty idempotencyKey.");
                }

                var (payment, refund) = await paymentService.RefundAsync(
                    buyerId, orderId, request.Amount, request.IdempotencyKey, cancellationToken);

                return Results.Created($"api/orders/{orderId}/refunds/{refund.PayPalRefundId}", new
                {
                    refundId = refund.PayPalRefundId,
                    orderId,
                    amount = refund.Amount,
                    status = refund.Status,
                    payment = PaymentStateDto.From(payment)
                });
            })
            .Produces(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}
