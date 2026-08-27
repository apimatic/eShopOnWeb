using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper by vaulting it with PayPal.
/// Full card details are never stored by this application.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user,
                IRepository<SavedCard> savedCardRepository, IPayPalGateway gateway) =>
            {
                return await HandleAsync(request, user, savedCardRepository, gateway);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ClaimsPrincipal user,
        IRepository<SavedCard> savedCardRepository, IPayPalGateway gateway)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Card is null || string.IsNullOrWhiteSpace(request.Card.Number)
            || string.IsNullOrWhiteSpace(request.Card.Expiry))
        {
            return Results.BadRequest("Card number and expiry (YYYY-MM) are required.");
        }

        var card = new CardDetails
        {
            Number = request.Card.Number,
            Expiry = request.Card.Expiry,
            SecurityCode = request.Card.SecurityCode,
            Name = request.Card.Name,
            AddressLine1 = request.Card.AddressLine1,
            AdminArea2 = request.Card.City,
            AdminArea1 = request.Card.State,
            PostalCode = request.Card.PostalCode,
            CountryCode = request.Card.CountryCode
        };

        var vaulted = await gateway.VaultCardAsync(card, payPalCustomerId: null,
            requestId: $"eshop-vault-{Guid.NewGuid():N}");

        var savedCard = await savedCardRepository.AddAsync(new SavedCard(buyerId,
            vaulted.VaultTokenId, vaulted.CustomerId, vaulted.Brand, vaulted.Last4, vaulted.Expiry));

        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = savedCard.Id,
            Brand = savedCard.Brand,
            Last4 = savedCard.Last4,
            Expiry = savedCard.Expiry
        };
        return Results.Created($"api/payment-methods/{savedCard.Id}", response);
    }
}
