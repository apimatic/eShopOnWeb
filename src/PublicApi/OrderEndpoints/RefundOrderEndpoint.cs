using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IShopOrderService>
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
            (int orderId, RefundOrderRequest request, IShopOrderService orders) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, orders);
            })
            .Produces<RefundResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IShopOrderService orders)
    {
        var buyerId = BuyerIdentity.Require(_httpContextAccessor);
        var result = await orders.RefundAsync(
            buyerId,
            request.OrderId,
            request.Amount,
            request.IdempotencyKey,
            _httpContextAccessor.HttpContext?.RequestAborted ?? default);
        return Results.Ok(RefundResponse.From(result, request.CorrelationId()));
    }
}
