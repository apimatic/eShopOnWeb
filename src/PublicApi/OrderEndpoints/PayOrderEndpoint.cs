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

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, ICheckoutService checkout, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = CurrentBuyer.Id(user);
                return await HandleAsync(request, checkout);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ICheckoutService checkout)
    {
        var card = request.Card is null ? null : PaymentRequestMapper.ToCardDetails(request.Card);
        var order = await checkout.PayAsync(request.OrderId, request.BuyerId!, card, request.PaymentMethodId);
        return Results.Ok(PaymentRequestMapper.ToOrderResponse(order));
    }
}

public class PayOrderRequest
{
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
    internal int OrderId { get; set; }
    internal string? BuyerId { get; set; }
}
