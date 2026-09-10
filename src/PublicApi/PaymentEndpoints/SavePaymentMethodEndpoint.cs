using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/payment-methods — saves a card for the signed-in shopper. The response describes the card
/// safely (never full card details) and carries the payment-method id as a top-level field.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SavePaymentMethodRequest request,
                ISavedCardService savedCardService,
                ClaimsPrincipal user,
                HttpContext http) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();
                if (request.Card is null)
                    return Results.BadRequest(new { message = "A card is required." });

                var view = await savedCardService.SaveCardAsync(buyerId, request.Card.ToCardDetails(), http.RequestAborted);
                return Results.Created($"api/payment-methods/{view.PaymentMethodId}", view);
            })
            .Produces<SavedCardView>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}
