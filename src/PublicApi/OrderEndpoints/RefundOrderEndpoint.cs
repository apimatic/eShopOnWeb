using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// A refund request. <see cref="Amount"/> null ⇒ refund the full remaining amount.
/// <see cref="IdempotencyKey"/> is caller-supplied: the same key never refunds twice; two distinct
/// keys make two distinct partial refunds.
/// </summary>
public class RefundOrderRequest
{
    public decimal? Amount { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

/// <summary>
/// POST /api/orders/{orderId}/refunds — refunds a captured payment, in full or in part. Shopper-scoped.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IPaymentService paymentService, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    return Results.BadRequest(new { message = "An idempotencyKey is required for a refund." });
                }

                var refund = await paymentService.RefundAsync(orderId, buyerId, request.Amount, request.IdempotencyKey!, ct);
                return Results.Ok(new RefundOrderResponse
                {
                    RefundId = refund.RefundId,
                    Status = refund.Status,
                    Amount = refund.Amount
                });
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }
}
