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

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderApiRequest, HttpContext>
{
    private readonly IOrderCheckoutService _checkout;

    public PayOrderEndpoint(IOrderCheckoutService checkout)
    {
        _checkout = checkout;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderApiRequest request, HttpContext httpContext) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, httpContext);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderApiRequest request, HttpContext httpContext)
    {
        var order = await _checkout.PayOrderAsync(new PayOrderRequest
        {
            BuyerId = httpContext.GetBuyerId(),
            OrderId = request.OrderId,
            PaymentMethodId = request.PaymentMethodId,
            Card = request.Card is null ? null : request.Card.ToCardDetails()
        });

        return Results.Ok(new PayOrderResponse { Order = OrderDtoMapper.ToDto(order) });
    }
}
