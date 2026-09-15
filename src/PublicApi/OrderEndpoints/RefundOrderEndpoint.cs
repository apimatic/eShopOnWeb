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

public class RefundOrderResponse
{
    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public string? PayPalRefundId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
}

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderCheckoutService>
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
            (int orderId, RefundOrderRequest request, IOrderCheckoutService checkout, HttpRequest httpRequest) =>
            {
                request.OrderId = orderId;
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
                    && httpRequest.Headers.TryGetValue("Idempotency-Key", out var headerKey))
                {
                    request.IdempotencyKey = headerKey.ToString();
                }

                return await HandleAsync(request, checkout);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderCheckoutService checkout)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.RequireBuyerId();
        var (order, refund) = await checkout.RefundAsync(request.OrderId, buyerId, request.Amount, request.IdempotencyKey);

        var response = new RefundOrderResponse
        {
            RefundId = refund.Id,
            OrderId = request.OrderId,
            PayPalRefundId = refund.PayPalRefundId,
            Status = refund.Status,
            Amount = refund.Amount,
            Currency = refund.Currency,
            PaymentStatus = order.PaymentStatus.ToString()
        };

        return Results.Created($"api/orders/{request.OrderId}/refunds/{response.RefundId}", response);
    }
}
