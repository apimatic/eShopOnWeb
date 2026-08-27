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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a fulfilled order's captured payment, in full or in part. The
/// caller-supplied idempotency key guarantees a repeated request never refunds twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null)
                {
                    return Results.Unauthorized();
                }
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    return Results.BadRequest("An idempotencyKey is required so a repeated request cannot refund twice.");
                }
                if (request.Amount.HasValue && request.Amount.Value <= 0)
                {
                    return Results.BadRequest("The refund amount must be positive.");
                }

                var refund = await orderPaymentService.RefundOrderAsync(buyerId, orderId, request.Amount,
                    request.IdempotencyKey, request.NoteToPayer, ct);
                if (refund is null)
                {
                    return Results.NotFound();
                }

                var response = new RefundOrderResponse(request.CorrelationId())
                {
                    RefundId = refund.PayPalRefundId,
                    OrderId = orderId,
                    Amount = refund.Amount,
                    Currency = refund.Currency,
                    Status = refund.Status
                };
                return Results.Created($"api/orders/{orderId}/refunds/{refund.Id}", response);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}
