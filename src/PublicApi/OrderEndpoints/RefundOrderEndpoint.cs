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
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public RefundDto Refund { get; set; } = new();
    public decimal RemainingRefundable { get; set; }
}

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ICheckoutService>
{
    private readonly IHttpContextAccessor _http;

    public RefundOrderEndpoint(IHttpContextAccessor http)
    {
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ICheckoutService checkout) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, checkout);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ICheckoutService checkout)
    {
        var http = _http.HttpContext!;
        var buyerId = HttpUser.RequireBuyerId(http);
        var key = request.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = http.Request.Headers["Idempotency-Key"].ToString();
        }

        var refund = await checkout.RefundAsync(request.OrderId, buyerId, request.Amount, key ?? string.Empty, http.RequestAborted);
        var order = await checkout.GetMyOrderAsync(request.OrderId, buyerId, http.RequestAborted);
        var response = new RefundOrderResponse
        {
            RefundId = refund.Id,
            OrderId = request.OrderId,
            Refund = RefundDto.From(refund),
            RemainingRefundable = order?.RemainingRefundable() ?? 0m
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
    }
}
