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

/// <summary>Saves a card for the signed-in shopper via PayPal's Vault API. Full card details are never stored locally.</summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService paymentMethodService) =>
            {
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, paymentMethodService);
            })
            .Produces<SavePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentMethodService paymentMethodService)
    {
        var response = new SavePaymentMethodResponse(request.CorrelationId());

        var paymentMethod = await paymentMethodService.SaveCardAsync(request.BuyerId, request.Card.ToCardDetails(), request.Alias);

        response.PaymentMethodId = paymentMethod.Id;
        response.PaymentMethod = PaymentMethodMapping.ToDto(paymentMethod);
        return Results.Created($"api/payment-methods/{paymentMethod.Id}", response);
    }
}
