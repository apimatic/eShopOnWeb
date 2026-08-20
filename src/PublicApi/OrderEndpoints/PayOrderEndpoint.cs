using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRouteRequest, IPaymentCheckoutService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest body, IPaymentCheckoutService payments) =>
            {
                return await HandleAsync(new PayOrderRouteRequest(orderId, body), payments);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRouteRequest request, IPaymentCheckoutService payments)
    {
        var order = await payments.PayAsync(
            EndpointUser.BuyerId(_httpContextAccessor.HttpContext!),
            request.OrderId,
            OrderResponseMapper.ToCardDetails(request.Body.Card),
            request.Body.PaymentMethodId);

        return Results.Ok(OrderResponseMapper.Map(order, payments.Currency));
    }
}

public class PayOrderRouteRequest
{
    public PayOrderRouteRequest(int orderId, PayOrderRequest body)
    {
        OrderId = orderId;
        Body = body;
    }

    public int OrderId { get; }
    public PayOrderRequest Body { get; }
}
