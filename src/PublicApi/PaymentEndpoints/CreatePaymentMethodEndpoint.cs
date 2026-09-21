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

/// <summary>Saves a card for the signed-in shopper. Returns the saved-card id and a safe descriptor only.</summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SavePaymentMethodRequest request,
                ClaimsPrincipal user,
                ISavedCardService service,
                CancellationToken ct) =>
            {
                if (request.Card is null)
                    throw new PaymentStateException("Card details are required to save a payment method.");

                var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
                var view = await service.SaveCardAsync(buyerId, request.Card.ToDomain()!, ct);
                return Results.Created($"/api/payment-methods/{view.PaymentMethodId}",
                    new CreatePaymentMethodResponse(view.PaymentMethodId, view.Brand, view.Last4, view.Expiry));
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
