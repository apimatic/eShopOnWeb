using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderApiRequest, HttpContext>
{
    private readonly IOrderCheckoutService _checkout;

    public RefundOrderEndpoint(IOrderCheckoutService checkout)
    {
        _checkout = checkout;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderApiRequest request, HttpContext httpContext) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, httpContext);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderApiRequest request, HttpContext httpContext)
    {
        var (order, refund) = await _checkout.RefundOrderAsync(new RefundOrderRequest
        {
            BuyerId = httpContext.GetBuyerId(),
            OrderId = request.OrderId,
            IdempotencyKey = request.IdempotencyKey,
            Amount = request.Amount
        });

        return Results.Ok(new RefundOrderResponse
        {
            RefundId = refund.Id,
            Order = OrderDtoMapper.ToDto(order)
        });
    }
}
