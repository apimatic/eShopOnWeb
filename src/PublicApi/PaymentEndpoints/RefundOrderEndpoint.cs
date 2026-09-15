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
/// POST /api/orders/{orderId}/refunds — returns a captured payment in full or in part. Carries a
/// caller-supplied idempotency key so a repeated request never refunds twice. Returns the refund id.
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
                IOrderPaymentService orderPaymentService,
                CancellationToken cancellationToken) =>
            await PaymentProblem.ExecuteAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    throw new PaymentException("An idempotency key is required for a refund.");
                }

                var buyerId = user.GetBuyerId();
                var refund = await orderPaymentService.RefundOrderAsync(
                    buyerId, orderId, request.Amount, request.IdempotencyKey, cancellationToken);

                return Results.Ok(new RefundResponse
                {
                    RefundId = refund.PayPalRefundId,
                    Amount = refund.Amount,
                    Status = refund.Status
                });
            }))
            .Produces<RefundResponse>()
            .WithTags("OrderEndpoints");
    }
}
