using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext http, ICheckoutService checkout) =>
            {
                request.OrderId = orderId;
                request.BuyerId = Caller.UserName(http);
                return await HandleAsync(request, checkout);
            })
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ICheckoutService checkout)
    {
        var card = request.Card is null ? null : OrderDtoMapper.ToCardDetails(request.Card);
        var order = await checkout.PayOrderAsync(
            request.BuyerId!,
            request.OrderId,
            new ApplicationCore.Interfaces.PayOrderRequest(card, request.PaymentMethodId));

        var response = new OrderActionResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = OrderDtoMapper.ToDto(order, checkout.Currency)
        };
        return Results.Ok(response);
    }
}
