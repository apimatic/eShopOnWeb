using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total. The request either carries one-off card details or names
/// one of the shopper's saved cards. The money is held, not taken. Idempotent per order.
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
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService)
    {
        var buyerId = user.GetBuyerId();

        var instruction = new PaymentInstruction(request.Card?.ToPayPalCard(), request.SavedCardId);
        var order = await paymentService.PayOrderAsync(buyerId, request.OrderId, instruction);

        return Results.Ok(OrderDto.From(order));
    }
}

public class PayOrderRequest
{
    /// <summary>Bound from the route; not part of the request body.</summary>
    public int OrderId { get; set; }

    /// <summary>One-off card details. Provide this or <see cref="SavedCardId"/>.</summary>
    public CardDetailsDto? Card { get; set; }

    /// <summary>The id of one of the shopper's saved cards to pay with instead of a one-off card.</summary>
    public int? SavedCardId { get; set; }
}
