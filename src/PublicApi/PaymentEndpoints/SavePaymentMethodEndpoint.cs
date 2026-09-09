using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Saves a card for the signed-in shopper. Returns a safe description, never card details.</summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SavePaymentMethodRequest request, ClaimsPrincipal user,
                IPaymentMethodService paymentMethodService, CancellationToken ct) =>
            {
                if (request?.Card is null)
                {
                    throw new PaymentException("Card details are required to save a payment method.");
                }

                var buyerId = user.GetBuyerId();
                var paymentMethod = await paymentMethodService.SaveCardAsync(buyerId,
                    request.Card.ToCardDetails(), ct);

                var response = PaymentMethodResponse.From(paymentMethod);
                return Results.Created($"api/payment-methods/{paymentMethod.Id}", response);
            })
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
