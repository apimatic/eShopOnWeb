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

/// <summary>Refunds the caller's captured order, in full or in part, under a caller-supplied idempotency key.</summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService svc, CancellationToken ct) =>
                await PaymentEndpointHelpers.Guarded(user, async buyerId =>
                {
                    var (summary, refundId) = await svc.RefundAsync(buyerId, orderId, request.Amount, request.IdempotencyKey, ct);
                    return Results.Ok(new RefundResponse(refundId, summary));
                }))
            .Produces<RefundResponse>()
            .WithTags("PaymentEndpoints");
    }
}
