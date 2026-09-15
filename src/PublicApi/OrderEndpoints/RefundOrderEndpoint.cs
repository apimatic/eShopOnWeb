using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Returns a captured payment, in full or in part, under a caller-supplied idempotency key.</summary>
public class RefundOrderRequest
{
    /// <summary>The amount to refund. Omit for the full remaining refundable amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key never refunds twice;
    /// two distinct partial refunds use two distinct keys.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>
/// POST /api/orders/{orderId}/refunds — refunds the caller's own fulfilled order. Returns the new
/// refund id as a top-level field.
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
                IOrderPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    throw new PaymentException("A refund requires an idempotencyKey.");
                }

                var buyerId = CurrentUser.BuyerId(user);
                var (order, refund) = await paymentService.RefundAsync(
                    buyerId, orderId, request.Amount, request.IdempotencyKey, cancellationToken);

                return Results.Ok(new
                {
                    refundId = refund.PayPalRefundId,
                    status = refund.Status,
                    amount = refund.Amount,
                    totalRefunded = order.Payment?.TotalRefunded,
                    orderStatus = order.Status.ToString(),
                    order = order.ToDto()
                });
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("OrderEndpoints");
    }
}
