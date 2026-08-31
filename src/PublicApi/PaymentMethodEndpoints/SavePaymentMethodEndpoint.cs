using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest : BaseRequest
{
    public CardDetailsRequest Card { get; set; } = new();
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(System.Guid correlationId) : base(correlationId) { }

    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

/// <summary>
/// Vaults a card with PayPal for the signed-in shopper. Only safe details (brand, last four
/// digits, expiry) are stored and returned — never the full card data.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ClaimsPrincipal>
{
    private readonly IPayPalGateway _payPalGateway;
    private readonly IRepository<SavedCard> _savedCardRepository;

    public SavePaymentMethodEndpoint(IPayPalGateway payPalGateway, IRepository<SavedCard> savedCardRepository)
    {
        _payPalGateway = payPalGateway;
        _savedCardRepository = savedCardRepository;
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
        var buyerId = user.Identity?.Name ?? string.Empty;
        var card = PayOrderEndpoint.MapCard(request.Card);
        if (card is null || string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentValidationException("Card number and expiry (YYYY-MM) are required.");
        }

        var token = await _payPalGateway.VaultCardAsync(card, $"eshop-vault-{buyerId}-{System.Guid.NewGuid():N}");

        var savedCard = new SavedCard(buyerId, token.PaymentTokenId, token.CustomerId,
            token.Brand, token.Last4, token.Expiry, card.CardholderName);
        savedCard = await _savedCardRepository.AddAsync(savedCard);

        var response = new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = savedCard.Id,
            Brand = savedCard.Brand,
            Last4 = savedCard.Last4,
            Expiry = savedCard.Expiry,
            CardholderName = savedCard.CardholderName
        };
        return Results.Created($"api/payment-methods/{savedCard.Id}", response);
    }
}
