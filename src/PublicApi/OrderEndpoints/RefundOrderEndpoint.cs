using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Returns a captured order, in full or in part, under a caller-supplied idempotency key. Repeating
/// a request under the same key does not refund twice; two distinct partial refunds are legitimate.
/// A partly-refunded order never becomes refundable beyond what was captured. Shopper-scoped.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                RefundOrderRequest? request,
                IOrderPaymentService orderPaymentService,
                IPaymentGateway gateway,
                ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                request ??= new RefundOrderRequest();
                var buyerId = user.GetBuyerId();

                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    throw new PaymentValidationException("A refund requires a caller-supplied idempotencyKey.");
                }

                var (order, refund) = await orderPaymentService.RefundAsync(
                    buyerId, orderId, request.Amount, request.IdempotencyKey!, cancellationToken);

                var response = new RefundOrderResponse
                {
                    RefundId = refund.PayPalRefundId,
                    OrderId = order.Id,
                    Amount = refund.Amount,
                    Status = refund.Status,
                    Payment = PaymentViewMapper.ToView(order.Payment!)
                };
                return Results.Created($"api/orders/{order.Id}/refunds/{refund.PayPalRefundId}", response);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}

public class RefundOrderRequest
{
    /// <summary>The amount to refund. Omit to refund the full remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating it never refunds twice.</summary>
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderResponse
{
    /// <summary>The identifier of the created refund.</summary>
    public string RefundId { get; set; } = string.Empty;

    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentView Payment { get; set; } = new();
}
