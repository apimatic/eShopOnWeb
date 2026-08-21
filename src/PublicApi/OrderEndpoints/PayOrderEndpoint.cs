using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest? request, IOrderPaymentService orders) =>
            {
                _httpContextAccessor.HttpContext!.Items["orderId"] = orderId;
                return await HandleAsync(request ?? new PayOrderRequest(), orders);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orders)
    {
        var http = _httpContextAccessor.HttpContext!;
        var orderId = (int)http.Request.RouteValues["orderId"]!;
        var buyerId = http.User.RequireBuyerId();
        var card = request.Card is null ? null : request.Card.ToCardDetails();
        var order = await orders.PayAsync(orderId, buyerId, card, request.PaymentMethodId);
        return Results.Ok(OrderResponseMapper.From(order));
    }
}
