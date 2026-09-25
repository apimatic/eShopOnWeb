using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>POST /api/orders/{orderId}/refunds — refund a fulfilled order in full or in part.</summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService, CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await PaymentProblem.RunAsync(async () =>
                {
                    var refund = await paymentService.RefundAsync(buyerId, orderId, request.Amount, request.IdempotencyKey, ct);
                    return Results.Created($"api/orders/{orderId}/refunds/{refund.RefundId}", new
                    {
                        refundId = refund.RefundId,
                        status = refund.Status,
                        amount = refund.Amount,
                        paymentStatus = refund.PaymentStatus,
                        totalRefunded = refund.TotalRefunded,
                    });
                });
            })
            .WithTags("PaymentEndpoints");
    }
}
