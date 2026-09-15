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
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Saves a card for the signed-in shopper.</summary>
public class CreatePaymentMethodRequest
{
    public CardModel Card { get; set; } = new();

    /// <summary>An optional shopper-friendly label for the card.</summary>
    public string? Label { get; set; }
}

/// <summary>
/// POST /api/payment-methods — vaults a card for the signed-in shopper. Returns the saved card id as
/// a top-level field, plus safe descriptors (brand, last four) — never full card details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreatePaymentMethodRequest request,
                ClaimsPrincipal user,
                ISavedPaymentMethodService paymentMethodService,
                CancellationToken cancellationToken) =>
            {
                if (request.Card is null || string.IsNullOrWhiteSpace(request.Card.Number))
                {
                    throw new PaymentException("Card details are required to save a card.");
                }

                var buyerId = CurrentUser.BuyerId(user);
                var saved = await paymentMethodService.SaveCardAsync(
                    buyerId, request.Card.ToCardDetails(), request.Label, cancellationToken);

                return Results.Created($"api/payment-methods/{saved.Id}", new
                {
                    paymentMethodId = saved.Id,
                    cardBrand = saved.CardBrand,
                    cardLast4 = saved.CardLast4,
                    expiry = saved.CardExpiry,
                    label = saved.Label
                });
            })
            .Produces(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
