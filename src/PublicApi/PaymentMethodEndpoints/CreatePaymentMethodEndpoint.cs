using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Vaults a card with PayPal for the signed-in shopper. The response identifies the
/// saved card and describes it safely (brand, last digits, expiry) — never full details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ClaimsPrincipal>
{
    private readonly ISavedCardService _savedCardService;

    public CreatePaymentMethodEndpoint(ISavedCardService savedCardService)
    {
        _savedCardService = savedCardService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }
        if (request.Card is null)
        {
            throw new PaymentDomainException("Card details are required to save a payment method.");
        }
        if (string.IsNullOrWhiteSpace(request.Card.Number) || request.Card.Number.Length is < 13 or > 19)
        {
            throw new PaymentDomainException("Card number must be 13-19 digits.");
        }
        if (string.IsNullOrWhiteSpace(request.Card.Expiry))
        {
            throw new PaymentDomainException("Card expiry is required (format: YYYY-MM).");
        }

        var savedCard = await _savedCardService.SaveCardAsync(buyerId, request.Card.ToPayPalCard());

        var response = new CreatePaymentMethodResponse(request.CorrelationId())
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

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDetailsDto? Card { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}
