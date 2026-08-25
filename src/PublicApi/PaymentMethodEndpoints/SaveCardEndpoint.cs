using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SaveCardEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SaveCardRequest req,
                   IRepository<SavedCard> cardRepo,
                   IPayPalService payPal,
                   ClaimsPrincipal user) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                if (string.IsNullOrEmpty(req.CardNumber) || string.IsNullOrEmpty(req.CardExpiry) ||
                    string.IsNullOrEmpty(req.CardCvv) || string.IsNullOrEmpty(req.BillingCountry))
                    return Results.BadRequest(new { error = "cardNumber, cardExpiry, cardCvv, and billingCountry are required." });

                var cardDetails = new CardDetails(
                    Number: req.CardNumber,
                    Expiry: req.CardExpiry,
                    Cvv: req.CardCvv,
                    CardholderName: req.CardholderName ?? "Cardholder",
                    BillingCountry: req.BillingCountry,
                    BillingStreet: req.BillingStreet,
                    BillingCity: req.BillingCity,
                    BillingState: req.BillingState,
                    BillingZip: req.BillingZip);

                VaultCardResult vaultResult;
                try
                {
                    vaultResult = await payPal.VaultCardAsync(cardDetails, null, buyerId);
                }
                catch (PayPalException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }

                var savedCard = new SavedCard(
                    buyerId,
                    vaultResult.VaultToken,
                    vaultResult.PayPalCustomerId,
                    vaultResult.CardBrand,
                    vaultResult.Last4,
                    vaultResult.Expiry);

                await cardRepo.AddAsync(savedCard);

                return Results.Created($"/api/payment-methods/{savedCard.Id}", new
                {
                    paymentMethodId = savedCard.Id,
                    cardBrand = savedCard.CardBrand,
                    last4 = savedCard.Last4,
                    expiry = savedCard.Expiry
                });
            })
            .WithTags("PaymentMethodEndpoints");
    }
}

public record SaveCardRequest(
    string? CardNumber,
    string? CardExpiry,
    string? CardCvv,
    string? CardholderName,
    string? BillingCountry,
    string? BillingStreet,
    string? BillingCity,
    string? BillingState,
    string? BillingZip);
