using System.Linq;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments.OrderEndpoints;

public class RefundOrderRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining refundable amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key: repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentStateDto Payment { get; set; } = new();
}

/// <summary>
/// POST /api/orders/{orderId}/refunds — return after fulfilment: refund the captured payment in
/// full or in part. Shopper-scoped to the caller's own order.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service,
             CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    throw new PaymentException("An idempotencyKey is required for a refund.");

                var buyerId = user.GetBuyerId();
                var refund = await service.RefundAsync(buyerId, orderId, request.Amount, request.IdempotencyKey, ct);

                // Re-read the payment so the response reflects the post-refund state.
                var orders = await service.GetOrdersForBuyerAsync(buyerId, ct);
                var view = orders.FirstOrDefault(o => o.Order.Id == orderId);

                var response = new RefundOrderResponse
                {
                    RefundId = refund.PayPalRefundId,
                    Amount = refund.Amount,
                    Status = refund.Status,
                    Payment = view is not null
                        ? PaymentMapping.ToStateDto(view.Payment)
                        : new PaymentStateDto { OrderId = orderId }
                };
                return Results.Created($"api/orders/{orderId}/refunds/{refund.PayPalRefundId}", response);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }
}
