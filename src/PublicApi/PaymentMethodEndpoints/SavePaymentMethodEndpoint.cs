using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. Only safe display fields
/// (brand, last digits, expiry) are stored and returned — never the full card.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly IRepository<SavedCard> _savedCardRepository;

    public SavePaymentMethodEndpoint(IPaymentGateway paymentGateway, IRepository<SavedCard> savedCardRepository)
    {
        _paymentGateway = paymentGateway;
        _savedCardRepository = savedCardRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(request, user, ct);
            })
            .Produces<SavePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        if (request.Card == null || string.IsNullOrWhiteSpace(request.Card.Number) ||
            string.IsNullOrWhiteSpace(request.Card.Expiry))
        {
            throw new PaymentDomainException("Card number and expiry are required.", 400);
        }

        var buyerId = user.Identity?.Name ?? string.Empty;
        var card = new CardDetails(request.Card.Number, request.Card.Expiry, request.Card.SecurityCode,
            request.Card.Name, request.Card.AddressLine1, request.Card.City, request.Card.State,
            request.Card.PostalCode, request.Card.CountryCode);

        var vaulted = await _paymentGateway.VaultCardAsync(card, buyerId,
            $"eshop-vault-{buyerId}-{Guid.NewGuid():N}", ct);

        var savedCard = new SavedCard(buyerId, vaulted.VaultTokenId, vaulted.CustomerId,
            vaulted.Brand, vaulted.LastDigits, vaulted.Expiry);
        await _savedCardRepository.AddAsync(savedCard, ct);

        var response = new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = savedCard.Id,
            Brand = savedCard.Brand,
            LastDigits = savedCard.LastDigits,
            Expiry = savedCard.Expiry
        };
        return Results.Created($"api/payment-methods/{savedCard.Id}", response);
    }
}
