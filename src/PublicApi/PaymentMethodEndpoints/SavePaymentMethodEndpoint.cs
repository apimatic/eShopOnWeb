using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper by vaulting it with PayPal. Only the PayPal
/// payment token id and safe display data (brand, last digits, expiry) are kept.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ClaimsPrincipal>
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _paymentGateway;

    public SavePaymentMethodEndpoint(IRepository<SavedCard> savedCardRepository, IPaymentGateway paymentGateway)
    {
        _savedCardRepository = savedCardRepository;
        _paymentGateway = paymentGateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<SavePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }
        if (request.Card == null || string.IsNullOrWhiteSpace(request.Card.Number) || string.IsNullOrWhiteSpace(request.Card.Expiry))
        {
            return Results.BadRequest("Card number and expiry are required.");
        }

        PayPalVaultedCard vaulted;
        try
        {
            vaulted = await _paymentGateway.VaultCardAsync(
                request.Card.ToGatewayModel(), buyerId, $"eshop-vault-{buyerId}-{Guid.NewGuid():N}");
        }
        catch (PayPalApiException ex)
        {
            return Results.UnprocessableEntity(
                $"PayPal could not save the card: {ex.Message} (debug id: {ex.DebugId}). The card was not saved.");
        }

        if (string.IsNullOrEmpty(vaulted.PaymentTokenId))
        {
            return Results.UnprocessableEntity("PayPal did not return a payment token. The card was not saved.");
        }

        var savedCard = new SavedCard(buyerId, vaulted.PaymentTokenId, vaulted.CustomerId, vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);
        savedCard = await _savedCardRepository.AddAsync(savedCard);

        var response = new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = savedCard.Id,
            Brand = savedCard.Brand,
            LastDigits = savedCard.LastDigits,
            Expiry = savedCard.Expiry,
            CardholderName = savedCard.CardholderName
        };
        return Results.Created($"api/payment-methods/{savedCard.Id}", response);
    }
}
