using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IOrderCheckoutService checkout, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = BuyerIdentity.Require(user);
                return await HandleAsync(request, checkout);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderCheckoutService checkout)
    {
        var card = request.Card is null ? null : PaymentRequestMapping.ToCardPayment(request.Card);
        var order = await checkout.PayAsync(request.BuyerId, request.OrderId, card, request.PaymentMethodId);
        return Results.Ok(OrderResponse.From(order));
    }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public int? PaymentMethodId { get; set; }
    public CardRequest? Card { get; set; }
}
