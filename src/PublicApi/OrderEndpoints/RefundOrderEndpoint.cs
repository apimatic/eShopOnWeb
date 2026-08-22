using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService orders) =>
            {
                return await HandleAsync(orderId, request, orders);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orders)
    {
        throw new System.InvalidOperationException("Order id is required.");
    }

    private async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, IOrderPaymentService orders)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new System.InvalidOperationException("HTTP context is not available.");
        var buyerId = httpContext.User.GetBuyerId();

        var idempotencyKey = request.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey)
            && httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var header))
        {
            idempotencyKey = header.ToString();
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required (body.idempotencyKey or Idempotency-Key header)." });
        }

        var refund = await orders.RefundAsync(
            buyerId,
            orderId,
            request.Amount,
            idempotencyKey,
            httpContext.RequestAborted);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Status = refund.Status,
            Amount = refund.Amount,
            OrderId = orderId
        };

        return Results.Created($"api/orders/{orderId}/refunds/{refund.Id}", response);
    }
}
