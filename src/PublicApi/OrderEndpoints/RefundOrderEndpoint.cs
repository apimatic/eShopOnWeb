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
    public RefundDto Refund { get; set; } = new();
    public OrderDto Order { get; set; } = new();
}

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
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
            (int orderId, RefundOrderRequest request, IOrderPaymentService orders) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, orders);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orders)
    {
        var buyerId = CreateOrderEndpoint.RequireBuyerId(_httpContextAccessor.HttpContext?.User);
        var result = await orders.RefundOrderAsync(buyerId, request.OrderId, request.Amount, request.IdempotencyKey);
        return Results.Ok(new RefundOrderResponse
        {
            RefundId = result.Refund.Id,
            OrderId = result.Order.Id,
            Refund = new RefundDto
            {
                RefundId = result.Refund.Id,
                PayPalRefundId = result.Refund.PayPalRefundId,
                Status = result.Refund.Status,
                Amount = result.Refund.Amount,
                Currency = result.Refund.Currency
            },
            Order = PaymentApiMapper.ToDto(result.Order)
        });
    }
}
