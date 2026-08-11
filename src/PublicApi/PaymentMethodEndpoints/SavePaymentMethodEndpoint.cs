using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Saves (vaults) a card for the signed-in shopper. The full card number is never stored.</summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SavePaymentMethodRequest request,
                ClaimsPrincipal user,
                ISavedCardService savedCardService,
                CancellationToken ct) =>
            {
                var buyerId = user.BuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var card = await savedCardService.SaveCardAsync(
                    buyerId, request.Card.ToCardDetails(), request.Label, ct);

                var response = new SavePaymentMethodResponse
                {
                    PaymentMethodId = card.Id,
                    Brand = card.Brand,
                    Last4 = card.Last4,
                    ExpiryMonth = card.ExpiryMonth,
                    ExpiryYear = card.ExpiryYear,
                    Label = card.Label
                };
                return Results.Created($"api/payment-methods/{card.Id}", response);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
