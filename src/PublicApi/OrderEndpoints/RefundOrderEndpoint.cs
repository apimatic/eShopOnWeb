using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRouteRequest, ICheckoutService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ICheckoutService checkout) =>
            {
                return await HandleAsync(new RefundOrderRouteRequest(orderId, request), checkout);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRouteRequest request, ICheckoutService checkout)
    {
        var http = _httpContextAccessor.HttpContext!;
        if (string.IsNullOrWhiteSpace(request.Body.IdempotencyKey))
        {
            throw new CheckoutException(400, "IdempotencyKey is required.");
        }

        var buyerId = http.RequireBuyerId();
        var refund = await checkout.RefundAsync(
            request.OrderId,
            buyerId,
            request.Body.IdempotencyKey.Trim(),
            request.Body.Amount,
            http.RequestAborted);

        var order = await checkout.GetOrderForBuyerAsync(request.OrderId, buyerId);
        return Results.Ok(new RefundOrderResponse
        {
            RefundId = refund.PayPalRefundId,
            OrderId = request.OrderId,
            Amount = refund.Amount,
            Status = refund.Status,
            Order = order is null ? new OrderDetailsDto { OrderId = request.OrderId } : OrderDtoMapper.ToDto(order)
        });
    }
}

public class RefundOrderRouteRequest
{
    public RefundOrderRouteRequest(int orderId, RefundOrderRequest body)
    {
        OrderId = orderId;
        Body = body;
    }

    public int OrderId { get; }
    public RefundOrderRequest Body { get; }
}
