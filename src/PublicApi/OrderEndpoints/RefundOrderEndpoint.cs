using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRouteRequest, IOrderCheckoutService>
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
            (int orderId, RefundOrderRequest request, IOrderCheckoutService checkout) =>
            {
                return await HandleAsync(new RefundOrderRouteRequest(orderId, request), checkout);
            })
            .Produces<CreateRefundResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRouteRequest request, IOrderCheckoutService checkout)
    {
        var buyerId = BuyerIdentity.RequireBuyerId(_httpContextAccessor.HttpContext!.User);
        var refund = await checkout.RefundAsync(buyerId, request.OrderId, request.Body.Amount, request.Body.IdempotencyKey);
        var order = await checkout.GetOrderAsync(buyerId, request.OrderId, requireOwner: true);
        return Results.Ok(new CreateRefundResponse
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Status = refund.Status,
            Amount = refund.Amount,
            Currency = refund.Currency,
            OrderStatus = order.Status.ToString()
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
