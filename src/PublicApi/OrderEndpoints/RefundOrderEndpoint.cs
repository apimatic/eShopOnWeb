using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Returns a fulfilled order: refunds the captured payment, in full or in part.
/// The caller-supplied idempotency key guarantees a repeated request never refunds twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, int, RefundOrderRequest, IOrderPaymentService>
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
            (int orderId, RefundOrderRequest request, IOrderPaymentService orderPaymentService) =>
            {
                return await HandleAsync(orderId, request, orderPaymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var refund = await orderPaymentService.RefundAsync(buyerId, orderId, request.Amount, request.IdempotencyKey);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.PayPalRefundId,
            OrderId = orderId,
            Amount = refund.Amount,
            Status = refund.Status
        };
        return Results.Ok(response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Partial amount; omit to refund the remaining captured balance in full.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}
