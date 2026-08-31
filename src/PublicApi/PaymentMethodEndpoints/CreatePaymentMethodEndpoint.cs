using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDetailsDto Card { get; set; } = new CardDetailsDto();
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Vaults a card with PayPal for the signed-in shopper. Only the vault token and
/// safe display metadata (brand, last digits, expiry) are stored - never the PAN/CVC.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request,
             ClaimsPrincipal user,
             IRepository<SavedPaymentMethod> paymentMethodRepository,
             IPayPalClient payPalClient) =>
            {
                return await HandleAsync(request, user, paymentMethodRepository, payPalClient);
            })
            .Produces<PaymentMethodDto>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ClaimsPrincipal user,
        IRepository<SavedPaymentMethod> paymentMethodRepository, IPayPalClient payPalClient)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Card.Number) || string.IsNullOrWhiteSpace(request.Card.Expiry))
        {
            return Results.BadRequest(new { message = "card.number and card.expiry are required." });
        }

        var card = new PayPalCardDetails(
            request.Card.Number,
            request.Card.Expiry,
            request.Card.SecurityCode,
            request.Card.Name,
            request.Card.BillingAddress == null ? null : new PayPalAddress(
                request.Card.BillingAddress.AddressLine1,
                request.Card.BillingAddress.City,
                request.Card.BillingAddress.State,
                request.Card.BillingAddress.PostalCode,
                request.Card.BillingAddress.CountryCode));

        var setupToken = await payPalClient.CreateSetupTokenAsync(card, $"setup-{Guid.NewGuid():N}");
        var paymentToken = await payPalClient.CreatePaymentTokenAsync(setupToken.SetupTokenId, $"vault-{Guid.NewGuid():N}");

        var savedCard = new SavedPaymentMethod(
            buyerId,
            paymentToken.PaymentTokenId,
            paymentToken.CustomerId ?? setupToken.CustomerId,
            paymentToken.Brand,
            paymentToken.LastDigits,
            paymentToken.Expiry,
            paymentToken.CardholderName);

        savedCard = await paymentMethodRepository.AddAsync(savedCard);

        return Results.Created($"api/payment-methods/{savedCard.Id}", Map(savedCard));
    }

    internal static PaymentMethodDto Map(SavedPaymentMethod savedCard) => new PaymentMethodDto
    {
        PaymentMethodId = savedCard.Id,
        Brand = savedCard.Brand,
        LastDigits = savedCard.LastDigits,
        Expiry = savedCard.Expiry,
        CardholderName = savedCard.CardholderName,
        CreatedAt = savedCard.CreatedAt
    };
}
