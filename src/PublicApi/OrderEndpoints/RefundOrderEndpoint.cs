using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderResponse
{
    public string RefundId { get; set; } = string.Empty;
    public RefundDto Refund { get; set; } = new();
    public OrderDto Order { get; set; } = new();
}

/// <summary>
/// Returns a fulfilled order in full or in part. Deduplicated by the caller-supplied idempotency
/// key. A partly-refunded order never becomes refundable beyond what was captured. Shopper-scoped:
/// acts only on the caller's own order. Returns the refund id as a top-level field.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var refund = await orderPaymentService.RefundAsync(
            request.BuyerId, request.OrderId, request.Amount, request.IdempotencyKey);

        // Reload so the response reflects the order's post-refund payment state.
        var order = await orderPaymentService.GetOrderForBuyerAsync(request.BuyerId, request.OrderId);

        var response = new RefundOrderResponse
        {
            RefundId = refund.RefundId,
            Refund = new RefundDto
            {
                RefundId = refund.RefundId,
                Amount = refund.Amount,
                Currency = refund.Currency,
                Status = refund.Status,
                CreatedAt = refund.CreatedAt
            },
            Order = order is null ? new OrderDto() : OrderDto.From(order)
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.RefundId}", response);
    }
}
