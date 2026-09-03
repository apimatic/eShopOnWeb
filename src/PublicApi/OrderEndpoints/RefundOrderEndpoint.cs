using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ICheckoutService>
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
                request.OrderId = orderId;
                return await HandleAsync(request, checkout);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ICheckoutService checkoutService)
    {
        var httpContext = _httpContextAccessor.HttpContext!;
        var outcome = await checkoutService.RefundAsync(
            new RefundOrderCommand(
                request.OrderId,
                HttpCaller.RequireUserName(httpContext),
                request.Amount,
                request.IdempotencyKey),
            httpContext.RequestAborted);
        var refund = outcome.Refund;
        var order = outcome.Order;

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            Refund = new OrderRefundDto
            {
                RefundId = refund.Id,
                PayPalRefundId = refund.PayPalRefundId,
                Status = refund.Status,
                Amount = refund.Amount,
                Currency = refund.Currency
            },
            Order = OrderDto.From(order)
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
    }
}
