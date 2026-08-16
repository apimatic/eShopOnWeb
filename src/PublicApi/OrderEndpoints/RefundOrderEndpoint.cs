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

public class RefundOrderRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining refundable balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key never refunds twice;
    /// two distinct partial refunds of the same capture use two distinct keys.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>
/// Refunds a captured payment on the shopper's own order, in full or in part. The total refunded
/// can never exceed what was captured. Returns the refund id.
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
                IPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var outcome = await paymentService.RefundAsync(buyerId, orderId, request.Amount,
                    request.IdempotencyKey, cancellationToken);

                return Results.Created($"api/orders/{orderId}/refunds/{outcome.RefundId}",
                    new { refundId = outcome.RefundId, order = outcome.Order });
            })
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("OrderEndpoints");
    }
}
