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

/// <summary>Response for a created refund; the refund id is a top-level field.</summary>
public class RefundResult
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// POST /api/orders/{orderId}/refunds — returns a fulfilled order in full or in part. The caller's
/// idempotency key prevents a repeat from refunding twice; two distinct partial refunds are allowed.
/// A partly-refunded order never becomes refundable beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IPaymentProcessingService service, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                var refund = await service.RefundOrderAsync(buyerId, orderId, request.Amount, request.IdempotencyKey, ct);

                return Results.Ok(new RefundResult
                {
                    RefundId = refund.RefundId,
                    Amount = refund.Amount,
                    Currency = refund.Currency,
                    Status = refund.Status,
                });
            })
            .Produces<RefundResult>()
            .WithTags("OrderEndpoints");
    }
}
