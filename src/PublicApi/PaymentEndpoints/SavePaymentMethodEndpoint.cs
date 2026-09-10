using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Saves (vaults) a card for the signed-in shopper. Never returns or stores full card details.</summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ISavedCardService service, ClaimsPrincipal user) =>
                await HandleAsync(request, service, user))
            .Produces<SavedCardResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService service, ClaimsPrincipal user)
    {
        var buyerId = CallerIdentity.Require(user);

        if (request.Card is null || !request.Card.HasAnyCardData)
            throw new PaymentValidationException("Card details are required to save a payment method.");

        var view = await service.SaveCardAsync(buyerId, request.Card.ToCardDetails(), CancellationToken.None);
        var response = SavedCardResponse.From(view);
        return Results.Created($"api/payment-methods/{response.PaymentMethodId}", response);
    }
}
