using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public class PayOrderRequest
{
    /// <summary>Card details for a one-off payment. Provide this OR <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDetailsDto? Card { get; set; }

    /// <summary>The id of one of the shopper's saved cards to pay with instead of raw card details.</summary>
    public int? SavedPaymentMethodId { get; set; }

    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}

/// <summary>
/// Authorizes (holds) the order total. Does not take the money — that happens at fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentProcessingService service) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null) return Results.Unauthorized();
                request.OrderId = orderId;
                request.BuyerId = buyerId;
                return await HandleAsync(request, service);
            })
            .Produces<OrderPaymentResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentProcessingService service)
    {
        var card = request.Card is null ? null : request.Card.ToPayPalCard();
        var instruction = new PayInstruction(card, request.SavedPaymentMethodId);

        var order = await service.PayAsync(request.OrderId, request.BuyerId, instruction);
        return Results.Ok(OrderPaymentMapping.ToResponse(order));
    }
}
