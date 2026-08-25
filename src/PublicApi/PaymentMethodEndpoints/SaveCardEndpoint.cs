using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SaveCardRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public string CardNumber { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string Cvv { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public string BillingCountryCode { get; set; } = string.Empty;
    public string? BillingAddressLine1 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
}

public class SaveCardEndpoint : IEndpoint<IResult, SaveCardRequest, IPayPalPaymentService>
{
    private readonly IRepository<SavedCard> _cardRepository;

    public SaveCardEndpoint(IRepository<SavedCard> cardRepository)
    {
        _cardRepository = cardRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SaveCardRequestBody body, ClaimsPrincipal user, IPayPalPaymentService paymentService) =>
            {
                var request = new SaveCardRequest
                {
                    BuyerId = user.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
                    CardNumber = body.CardNumber ?? string.Empty,
                    Expiry = body.Expiry ?? string.Empty,
                    Cvv = body.Cvv ?? string.Empty,
                    CardholderName = body.CardholderName,
                    BillingCountryCode = body.BillingCountryCode ?? string.Empty,
                    BillingAddressLine1 = body.BillingAddressLine1,
                    BillingCity = body.BillingCity,
                    BillingState = body.BillingState,
                    BillingPostalCode = body.BillingPostalCode
                };
                return await HandleAsync(request, paymentService);
            })
            .Produces<object>(201)
            .Produces(400)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SaveCardRequest request, IPayPalPaymentService paymentService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        if (string.IsNullOrEmpty(request.CardNumber) || string.IsNullOrEmpty(request.Expiry)
            || string.IsNullOrEmpty(request.Cvv) || string.IsNullOrEmpty(request.BillingCountryCode))
            return Results.BadRequest(new { error = "cardNumber, expiry, cvv, and billingCountryCode are required." });

        var card = new CardDetails(
            Number: request.CardNumber,
            Expiry: request.Expiry,
            SecurityCode: request.Cvv,
            Name: request.CardholderName ?? string.Empty,
            CountryCode: request.BillingCountryCode,
            AddressLine1: request.BillingAddressLine1,
            City: request.BillingCity,
            State: request.BillingState,
            PostalCode: request.BillingPostalCode);

        VaultResult result;
        try
        {
            result = await paymentService.VaultCardAsync(request.BuyerId, card, CancellationToken.None);
        }
        catch (PayPalException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode);
        }

        var savedCard = new SavedCard(request.BuyerId, result.VaultId, result.PayPalCustomerId, result.Last4, result.Brand, result.Expiry);
        savedCard = await _cardRepository.AddAsync(savedCard);

        return Results.Created($"/api/payment-methods/{savedCard.Id}", new { paymentMethodId = savedCard.Id });
    }
}

public class SaveCardRequestBody
{
    public string? CardNumber { get; set; }
    public string? Expiry { get; set; }
    public string? Cvv { get; set; }
    public string? CardholderName { get; set; }
    public string? BillingCountryCode { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
}
