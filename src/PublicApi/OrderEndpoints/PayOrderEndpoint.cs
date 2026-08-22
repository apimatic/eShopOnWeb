using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRouteRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IPaymentService payments, HttpContext httpContext) =>
            {
                var route = new PayOrderRouteRequest
                {
                    OrderId = orderId,
                    BuyerId = httpContext.User.GetBuyerId(),
                    PaymentMethodId = request.PaymentMethodId,
                    Card = request.Card
                };
                return await HandleAsync(route, payments);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRouteRequest request, IPaymentService payments)
    {
        var result = await payments.PayAsync(
            request.BuyerId,
            request.OrderId,
            request.Card?.ToCardPaymentRequest(),
            request.PaymentMethodId);
        return Results.Ok(OrderResponseMapper.From(result));
    }
}

public class PayOrderRouteRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public int? PaymentMethodId { get; set; }
    public CardDetailsRequest? Card { get; set; }
}
