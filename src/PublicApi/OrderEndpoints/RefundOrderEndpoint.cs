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
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, HttpContext http, ICheckoutService checkout) =>
            {
                request.OrderId = orderId;
                request.BuyerId = Caller.UserName(http);
                return await HandleAsync(request, checkout);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ICheckoutService checkout)
    {
        var (order, refund) = await checkout.RefundOrderAsync(
            request.BuyerId!,
            request.OrderId,
            new ApplicationCore.Interfaces.RefundOrderRequest(request.Amount, request.IdempotencyKey));

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            RefundId = refund.PayPalRefundId,
            Order = OrderDtoMapper.ToDto(order, checkout.Currency)
        };
        return Results.Ok(response);
    }
}
