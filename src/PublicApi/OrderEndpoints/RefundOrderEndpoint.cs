using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds the shopper's own fulfilled order, in full or in part. The caller-supplied idempotency key makes a
/// repeat under the same key return the original refund; two distinct partial refunds remain legitimate. A
/// partly-refunded order never becomes refundable beyond what was captured. Returns <c>refundId</c> at the top level.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundRequest request, ClaimsPrincipal user,
                IOrderPaymentService orderPaymentService, CancellationToken cancellationToken) =>
            {
                var buyerId = user.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                if (request == null || string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    return Results.BadRequest(new { error = "An idempotency key is required for refunds." });
                }

                var outcome = await orderPaymentService.RefundOrderAsync(buyerId, orderId, request.Amount, request.IdempotencyKey, cancellationToken);
                var refund = outcome.Refund;

                var body = new
                {
                    refundId = refund.RefundId,
                    amount = refund.Amount,
                    status = refund.Status,
                    replay = outcome.WasReplay
                };
                return Results.Ok(body);
            })
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }
}
