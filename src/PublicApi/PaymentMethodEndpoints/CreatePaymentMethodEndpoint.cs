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

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The card is stored at PayPal; only a safe
/// descriptor comes back. Returns the new payment-method id as a top-level field.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService paymentMethodService) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, paymentMethodService);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentMethodService paymentMethodService)
    {
        var paymentMethod = await paymentMethodService.SaveCardAsync(request.BuyerId, request.Card.ToCardDetails());
        var response = new SavePaymentMethodResponse
        {
            PaymentMethodId = paymentMethod.Id,
            PaymentMethod = PaymentMethodDto.From(paymentMethod)
        };
        return Results.Created($"api/payment-methods/{paymentMethod.Id}", response);
    }
}
