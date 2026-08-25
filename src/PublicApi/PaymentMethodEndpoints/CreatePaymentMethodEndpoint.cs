using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Saves a card to PayPal's vault for the signed-in shopper to reuse later. The card
/// number itself is never stored by this application.</summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CardDetailsDto card, ClaimsPrincipal user, IPaymentMethodService paymentMethodService) =>
            {
                var request = new CreatePaymentMethodRequest(user.Identity?.Name ?? string.Empty, card);
                return await HandleAsync(request, paymentMethodService);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentMethodService paymentMethodService)
    {
        var response = new CreatePaymentMethodResponse(request.CorrelationId());

        var paymentMethod = await paymentMethodService.SaveCardAsync(request.BuyerId, request.Card.ToPayPalCardDetails());

        response.PaymentMethodId = paymentMethod.Id;
        response.PaymentMethod = PaymentMethodDto.FromEntity(paymentMethod);
        return Results.Created($"api/payment-methods/{paymentMethod.Id}", response);
    }
}
