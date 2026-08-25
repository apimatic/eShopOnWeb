using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IRepository<SavedCard>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request, HttpContext ctx,
                   IRepository<SavedCard> savedCardRepo,
                   IPayPalService payPalService) =>
            {
                request.BuyerId = ctx.User.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, savedCardRepo, payPalService);
            })
            .Produces<SavePaymentMethodResponse>(201)
            .Produces(400)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(SavePaymentMethodRequest request, IRepository<SavedCard> repo)
        => throw new NotSupportedException();

    private static async Task<IResult> HandleAsync(
        SavePaymentMethodRequest request,
        IRepository<SavedCard> savedCardRepo,
        IPayPalService payPalService)
    {
        if (string.IsNullOrEmpty(request.BuyerId)) return Results.Unauthorized();
        if (request.Card == null)
            return Results.BadRequest(new { error = "Card details are required." });
        if (string.IsNullOrWhiteSpace(request.Card.Number))
            return Results.BadRequest(new { error = "Card number is required." });
        if (string.IsNullOrWhiteSpace(request.Card.Expiry))
            return Results.BadRequest(new { error = "Card expiry is required." });

        var cardSource = new PayPalCardSource(
            request.Card.Number,
            request.Card.Expiry,
            request.Card.SecurityCode ?? string.Empty,
            request.Card.Name,
            request.Card.AddressLine1,
            request.Card.City,
            request.Card.State,
            request.Card.CountryCode ?? "US",
            request.Card.PostalCode);

        var idempotencyKey = $"vault-{request.BuyerId}-{Guid.NewGuid():N}";

        try
        {
            var vaultResult = await payPalService.VaultCardAsync(
                request.BuyerId, cardSource, idempotencyKey);

            var savedCard = new SavedCard(
                request.BuyerId,
                vaultResult.VaultTokenId,
                vaultResult.LastFourDigits,
                vaultResult.CardBrand,
                vaultResult.Expiry,
                vaultResult.CardType);

            savedCard = await savedCardRepo.AddAsync(savedCard);

            return Results.Created($"api/payment-methods/{savedCard.Id}",
                new SavePaymentMethodResponse
                {
                    PaymentMethodId = savedCard.Id,
                    LastFourDigits = savedCard.LastFourDigits,
                    CardBrand = savedCard.CardBrand,
                    Expiry = savedCard.Expiry,
                    CardType = savedCard.CardType
                });
        }
        catch (PayPalProviderException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.Problem("PayPal returned an unreadable response.", statusCode: 502);
        }
    }
}
