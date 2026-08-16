using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Paypal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/refunds — the shopper returns their own fulfilled order, refunding the
/// captured payment in full or in part. The caller-supplied idempotency key makes a repeat a no-op.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundRequest request, ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
            {
                var buyerId = RequestMapper.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    return Results.BadRequest(new { message = "An idempotencyKey is required for a refund." });

                var (order, refund) = await service.RefundAsync(buyerId, orderId,
                    new RefundOrderInput(request.Amount, request.IdempotencyKey), ct);

                return Results.Ok(new
                {
                    refundId = refund.PayPalRefundId,
                    orderId = order.Id,
                    order = PaymentMapper.ToDto(order)
                });
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("OrderEndpoints");
    }
}
