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
/// Refunds the caller's captured payment, in full or in part. The caller-supplied idempotency key
/// makes a repeat request a no-op while allowing distinct partial refunds. Returns the refund id.
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
                IOrderPaymentService service,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    throw new PaymentStateException("A refund requires a non-empty idempotencyKey.");

                var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
                var view = await service.RefundAsync(buyerId, orderId, request.Amount, request.IdempotencyKey!, ct);
                var refundId = view.PayPalRefundId ?? view.Id.ToString();
                return Results.Ok(new CreateRefundResponse(refundId, view));
            })
            .Produces<CreateRefundResponse>()
            .WithTags("PaymentEndpoints");
    }
}
