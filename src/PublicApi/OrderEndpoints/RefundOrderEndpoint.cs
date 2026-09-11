using System;
using System.Security.Claims;
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
/// Refunds a fulfilled order's captured payment, in full or in part. Carries a caller-supplied
/// idempotency key: repeating the request under the same key never refunds twice, while two
/// distinct partial refunds remain legitimate. A partly-refunded order is never refundable beyond
/// what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ClaimsPrincipal, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, paymentService);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService)
    {
        var buyerId = user.GetBuyerId();

        // Fall back to a fresh key only if the caller omits one, so an accidental omission still
        // produces a valid (if non-idempotent) refund rather than an error.
        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : request.IdempotencyKey!;

        var (order, refundId) = await paymentService.RefundOrderAsync(buyerId, request.OrderId, request.Amount, idempotencyKey);

        return Results.Created($"api/orders/{request.OrderId}/refunds/{refundId}", new RefundOrderResponse
        {
            RefundId = refundId,
            Order = OrderDto.From(order)
        });
    }
}

public class RefundOrderRequest
{
    /// <summary>Bound from the route; not part of the request body.</summary>
    public int OrderId { get; set; }

    /// <summary>Amount to refund. Omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating under the same key never refunds twice.</summary>
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderResponse
{
    /// <summary>The identifier of the refund that was created (or the existing one for a repeated key).</summary>
    public int RefundId { get; set; }
    public OrderDto Order { get; set; } = new();
}
