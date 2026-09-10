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

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    /// <summary>Card details for a one-off payment. Mutually exclusive with <see cref="SavedCardId"/>.</summary>
    public CardDto? Card { get; set; }
    /// <summary>Id of one of the shopper's saved cards to pay with.</summary>
    public int? SavedCardId { get; set; }
}

/// <summary>
/// POST /api/orders/{orderId}/pay — authorizes (holds) the order total using a one-off card or a
/// saved card. Does not take the money. Shopper-scoped and idempotent in effect.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, paymentService);
            })
            .Produces<OrderPaymentDto>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user,
        IPaymentService paymentService)
    {
        var buyerId = CallerId.BuyerId(user);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Card is null && request.SavedCardId is null)
        {
            return Results.BadRequest(new
            {
                message = "Provide either card details or a saved card id to pay with."
            });
        }

        var instruction = new PayInstruction
        {
            Card = request.Card is null ? null : PaymentMapping.ToGatewayCard(request.Card),
            SavedCardId = request.SavedCardId
        };

        var view = await paymentService.AuthorizeOrderAsync(buyerId, request.OrderId, instruction);
        return Results.Ok(PaymentMapping.ToDto(view));
    }
}
