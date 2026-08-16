using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderRequest
{
    /// <summary>Card details for a one-off payment. Provide this OR <see cref="PaymentMethodId"/>.</summary>
    public CardModel? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards. Provide this OR <see cref="Card"/>.</summary>
    public int? PaymentMethodId { get; set; }
}

/// <summary>
/// POST /api/orders/{orderId}/pay — authorizes (holds) the order total against a card or a
/// saved card. Does not take the money. Idempotent: a double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderEndpoint.Request, IOrderPaymentService>
{
    public record Request(int OrderId, string BuyerId, PayOrderRequest Body);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest body, ClaimsPrincipal user, IOrderPaymentService paymentService) =>
                await HandleAsync(new Request(orderId, user.GetBuyerId(), body ?? new PayOrderRequest()), paymentService))
            .Produces<OrderSummaryDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(Request request, IOrderPaymentService paymentService)
    {
        var card = request.Body.Card?.ToDetails();
        var order = await paymentService.AuthorizeAsync(request.BuyerId, request.OrderId, card, request.Body.PaymentMethodId);
        return Results.Ok(PaymentDtoMapper.ToDto(order));
    }
}
