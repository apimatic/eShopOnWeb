using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>POST /api/orders/{orderId}/refunds — refund a captured payment, full or partial, under a caller idempotency key (shopper-scoped).</summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        async (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IPaymentService service, CancellationToken ct) =>
            {
                var buyerId = CallerContext.BuyerId(user);
                var outcome = await service.RefundAsync(buyerId, orderId, request.Amount, request.IdempotencyKey, ct);

                return Results.Ok(new RefundOrderResponse
                {
                    RefundId = outcome.RefundId,
                    PayPalRefundId = outcome.PayPalRefundId,
                    Order = outcome.Order
                });
            })
            .Produces<RefundOrderResponse>()
            .WithTags("PaymentEndpoints");
    }
}
