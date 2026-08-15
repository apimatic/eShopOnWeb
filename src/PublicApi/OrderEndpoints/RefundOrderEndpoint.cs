using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = default!;

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = default!;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = default!;
    public PaymentDto Payment { get; set; } = default!;
}

/// <summary>
/// Returns money after fulfilment: refunds the captured payment, in full or in part. Never refunds beyond
/// what was captured; repeating the same idempotency key does not refund twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    return Results.BadRequest(new { message = "An idempotencyKey is required for refunds." });
                request.OrderId = orderId;
                request.BuyerId = buyerId;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var (order, refund) = await orderPaymentService.RefundAsync(request.OrderId, request.BuyerId!, request.Amount, request.IdempotencyKey);

        var response = new RefundOrderResponse
        {
            RefundId = refund.RefundId,
            OrderId = order.Id,
            Amount = refund.Amount,
            Status = refund.Status,
            Payment = OrderPaymentMapper.ToDto(order.Payment!)
        };
        return Results.Created($"api/orders/{order.Id}/refunds/{refund.RefundId}", response);
    }
}
