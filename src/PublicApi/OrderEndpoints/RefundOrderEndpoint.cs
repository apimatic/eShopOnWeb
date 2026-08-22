using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public OrderResponse Order { get; set; } = new();
}

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ICheckoutPaymentService>
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
            (int orderId, RefundOrderRequest request, ICheckoutPaymentService checkout, HttpContext httpContext) =>
            {
                request.OrderId = orderId;
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
                    && httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var headerKey))
                {
                    request.IdempotencyKey = headerKey.ToString();
                }
                return await HandleAsync(request, checkout);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ICheckoutPaymentService checkout)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name ?? string.Empty;
        var (order, refund) = await checkout.RefundAsync(
            request.OrderId,
            buyerId,
            request.Amount,
            request.IdempotencyKey,
            default);

        return Results.Ok(new RefundOrderResponse
        {
            RefundId = refund.Id,
            OrderId = order.Id,
            Order = OrderResponseMapper.Map(order)
        });
    }
}
