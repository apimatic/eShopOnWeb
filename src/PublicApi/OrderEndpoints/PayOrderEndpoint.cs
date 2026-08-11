using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest
{
    public int OrderId { get; set; }

    /// <summary>Raw card details for a one-off payment. Provide this OR <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards to pay with. Provide this OR <see cref="Card"/>.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

/// <summary>
/// POST /api/orders/{orderId}/pay — authorize (hold) the order total. Does not capture. Idempotent
/// per order. The request carries card details or names one of the shopper's saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, service, user);
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();

        var hasCard = request.Card is not null;
        var hasSaved = request.SavedPaymentMethodId is not null;
        if (hasCard == hasSaved)
        {
            throw new InvalidPaymentRequestException(
                "Provide either card details or a saved card id (exactly one).");
        }

        var instruction = hasCard
            ? new PaymentInstruction(Card: request.Card!.ToRawCard())
            : new PaymentInstruction(SavedPaymentMethodId: request.SavedPaymentMethodId);

        var order = await service.PayAsync(request.OrderId, buyerId, instruction);
        return Results.Ok(order.ToDto());
    }
}
