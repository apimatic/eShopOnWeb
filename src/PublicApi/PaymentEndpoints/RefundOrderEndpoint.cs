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
/// POST /api/orders/{orderId}/refunds — refunds the caller's own captured order, in full or in part.
/// Carries a caller-supplied idempotency key: repeating a request under the same key never refunds twice,
/// while two distinct partial refunds remain legitimate. Returns the refund id as a top-level field.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId, RefundApiRequest request, IPaymentService service, ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    throw new PaymentStateException("An idempotency key is required for a refund.");

                var refund = await service.RefundAsync(orderId, user.BuyerId(), request.Amount,
                    request.IdempotencyKey, ct);

                return Results.Ok(new
                {
                    refundId = refund.RefundId,
                    orderId = refund.OrderId,
                    amount = refund.Amount,
                    status = refund.Status,
                    refundedTotal = refund.RefundedTotal,
                    refundableRemaining = refund.RefundableRemaining
                });
            })
            .WithTags("OrderPaymentEndpoints");
    }
}
