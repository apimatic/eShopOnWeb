using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves (vaults) a card with PayPal for the signed-in shopper and stores only a safe reference to it
/// (vault token + brand/last4/expiry). Full card details are never stored in this app's database.
/// A repeated save of the same card returns the existing saved card rather than creating a duplicate.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, ClaimsPrincipal user,
                   IRepository<SavedPaymentMethod> paymentMethodRepository, IPaymentService paymentService,
                   CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var card = request.Card;
                if (card is null || !card.HasCardNumber)
                {
                    return Results.BadRequest("Card number, expiry and security code are required.");
                }

                // Idempotent save: if this shopper already has a card with the same last4 + expiry, return it.
                var existingCards = await paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
                var last4 = card.LastFour();
                var expiry = card.Expiry.Trim();
                var duplicate = existingCards.FirstOrDefault(c => c.LastFourDigits == last4 && c.Expiry == expiry);
                if (duplicate is not null)
                {
                    return Results.Ok(ToDto(duplicate));
                }

                var idempotencyKey = $"vault-{buyerId}-{card.Fingerprint()}";
                var vaulted = await paymentService.VaultCardAsync(card.ToCardDetails(), idempotencyKey, ct);

                var saved = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.Brand, vaulted.LastFourDigits, vaulted.Expiry, request.Alias);
                saved = await paymentMethodRepository.AddAsync(saved, ct);

                return Results.Created($"api/payment-methods/{saved.Id}", ToDto(saved));
            })
            .Produces<SavedCardDto>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Saves a card for the signed-in shopper", "Vaults a card with PayPal and returns a safe descriptor."));
    }

    private static SavedCardDto ToDto(SavedPaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        Brand = pm.CardBrand,
        Last4 = pm.LastFourDigits,
        Expiry = pm.Expiry,
        Alias = pm.Alias
    };
}
