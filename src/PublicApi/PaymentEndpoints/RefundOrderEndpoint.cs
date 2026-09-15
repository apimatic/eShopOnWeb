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

/// <summary>
/// A return after fulfilment. <see cref="Amount"/> is optional — omit it for a full refund of
/// the remaining refundable amount. <see cref="IdempotencyKey"/> makes a repeated request a
/// no-op; it may also be supplied via the <c>Idempotency-Key</c> header.
/// </summary>
public class RefundOrderRequest
{
    public decimal? Amount { get; set; }
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// Refunds a captured payment, in full or in part. Shopper-scoped. A partly-refunded order is
/// never refundable beyond what was captured; a repeat under the same key never refunds twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                RefundOrderRequest request,
                HttpContext http,
                ClaimsPrincipal user,
                IPaymentService paymentService,
                CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null) return Results.Unauthorized();

                var idempotencyKey = request.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(idempotencyKey) &&
                    http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
                {
                    idempotencyKey = header.ToString();
                }
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                    return Results.BadRequest(new { errors = new[] { "An idempotency key is required for refunds (body 'idempotencyKey' or 'Idempotency-Key' header)." } });

                var result = await paymentService.RefundOrderAsync(buyerId, orderId, request.Amount, idempotencyKey!, ct);
                if (!result.IsSuccess) return result.ToProblem();

                var refund = result.Value;
                return Results.Created($"api/orders/{orderId}/refunds/{refund.RefundId}", new
                {
                    refundId = refund.RefundId,
                    orderId,
                    amount = refund.Amount,
                    status = refund.Status,
                    createdAt = refund.CreatedAt
                });
            })
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .WithTags("OrderEndpoints");
    }
}
