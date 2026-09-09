using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public record SavePaymentMethodRequest(CardInput? Card, string? Alias);

public class SavePaymentMethodContext
{
    public string BuyerId { get; init; } = string.Empty;
    public SavePaymentMethodRequest Request { get; init; } = new(null, null);
    public CancellationToken Ct { get; init; }
}

/// <summary>Saves (vaults) a card for the signed-in shopper and returns a safe descriptor of it.</summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodContext>
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalPaymentGateway _gateway;

    public SavePaymentMethodEndpoint(IRepository<SavedCard> savedCardRepository, IPayPalPaymentGateway gateway)
    {
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, CancellationToken ct) =>
                await HandleAsync(new SavePaymentMethodContext { BuyerId = user.GetBuyerId(), Request = request, Ct = ct }))
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodContext context)
    {
        var card = context.Request.Card;
        if (card is null || string.IsNullOrWhiteSpace(card.Number) ||
            string.IsNullOrWhiteSpace(card.Expiry) || string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            return Results.BadRequest(new { message = "Card number, expiry (YYYY-MM) and security code are required." });
        }

        // Reuse the shopper's existing PayPal customer id so all their cards attach to one customer.
        var existing = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(context.BuyerId));
        var existingCustomerId = existing.FirstOrDefault()?.PayPalCustomerId;

        try
        {
            var result = await _gateway.VaultCardAsync(
                new VaultCardCommand(existingCustomerId, card.ToCardDetails(), Guid.NewGuid().ToString("N")),
                context.Ct);

            var savedCard = new SavedCard(context.BuyerId, result.CustomerId, result.VaultId,
                result.Card.Brand, result.Card.Last4, result.Card.Expiry, context.Request.Alias);
            await _savedCardRepository.AddAsync(savedCard);

            var response = new SavePaymentMethodResponse
            {
                PaymentMethodId = savedCard.Id,
                Brand = savedCard.Brand,
                Last4 = savedCard.Last4,
                Expiry = savedCard.Expiry,
                Alias = savedCard.Alias
            };
            return Results.Created($"api/payment-methods/{savedCard.Id}", response);
        }
        catch (PaymentGatewayException ex)
        {
            return PaymentResults.FromGatewayException(ex);
        }
    }
}

public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? Alias { get; set; }
}
