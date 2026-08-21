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

public class PayOrderRequest
{
    public int? PaymentMethodId { get; set; }
    public CardDetailsRequest? Card { get; set; }
}

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IOrderCheckoutService checkout, ClaimsPrincipal user) =>
            {
                request ??= new PayOrderRequest();
                var card = request.Card is null ? null : request.Card.ToCardSource();
                var order = await checkout.PayAsync(user.GetBuyerId(), orderId, card, request.PaymentMethodId);
                return Results.Ok(OrderResponseMapper.From(order));
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderCheckoutService checkout) =>
        Task.FromResult(Results.BadRequest());
}
