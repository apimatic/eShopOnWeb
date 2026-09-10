using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderRequest
{
    public int OrderId { get; set; }

    /// <summary>Amount to refund. Omit for a full refund of the remaining refundable amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key does not refund twice;
    /// two distinct partial refunds of the same capture use two different keys.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public OrderDto? Order { get; set; }
}

/// <summary>
/// Refunds a fulfilled order, in full or in part. Shopper-scoped (acts only on the caller's own order)
/// and idempotent per caller-supplied key. A partly-refunded order can never be refunded beyond capture.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ClaimsPrincipal, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, service);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentConflictException("A refund requires a non-empty idempotencyKey.");
        }

        var buyerId = user.GetBuyerId();
        var (refund, order) = await service.RefundAsync(buyerId, request.OrderId, request.Amount, request.IdempotencyKey);

        var response = new RefundOrderResponse
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Amount = refund.Amount,
            Status = refund.Status,
            Order = OrderDto.From(order)
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
    }
}
