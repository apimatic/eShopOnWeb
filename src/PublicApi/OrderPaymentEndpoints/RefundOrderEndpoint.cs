using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public class RefundOrderRequest
{
    /// <summary>The amount to refund. Omit for a full refund of the remaining refundable amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = string.Empty;
    public OrderPaymentResponse Order { get; set; } = new();
}

/// <summary>
/// Refunds a fulfilled order, in full or in part, under a caller-supplied idempotency key. Acts only
/// on the caller's own order.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IPaymentProcessingService service) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null) return Results.Unauthorized();
                request.OrderId = orderId;
                request.BuyerId = buyerId;
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentProcessingService service)
    {
        var outcome = await service.RefundAsync(request.OrderId, request.BuyerId, request.Amount, request.IdempotencyKey);
        return Results.Ok(new RefundOrderResponse
        {
            RefundId = outcome.RefundId,
            Order = OrderPaymentMapping.ToResponse(outcome.Order)
        });
    }
}
