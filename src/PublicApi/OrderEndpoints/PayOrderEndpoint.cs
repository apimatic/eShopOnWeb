using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest>
{
    private readonly IOrderPaymentService _orders;

    public PayOrderEndpoint(IOrderPaymentService orders)
    {
        _orders = orders;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(orderId, request, httpContext);
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request) => Task.FromResult(Results.BadRequest());

    public async Task<IResult> HandleAsync(int orderId, PayOrderRequest request, HttpContext httpContext)
    {
        var buyerId = PaymentRequestMapper.RequireBuyerId(httpContext);
        var card = request.Card is null ? null : PaymentRequestMapper.ToCardDetails(request.Card);
        var order = await _orders.PayAsync(orderId, buyerId, card, request.PaymentMethodId, httpContext.RequestAborted);
        return Results.Ok(order.ToDto());
    }
}
