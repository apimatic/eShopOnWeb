using System;
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

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved card
/// and describes it safely (brand, last digits, expiry) — never full card details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedCardService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreatePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ISavedCardService savedCardService) =>
            {
                return await HandleAsync(request, savedCardService);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedCardService savedCardService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }
        if (request.Card is null || string.IsNullOrWhiteSpace(request.Card.Number)
            || string.IsNullOrWhiteSpace(request.Card.Expiry))
        {
            return Results.BadRequest("'card' with a number and expiry (YYYY-MM) is required.");
        }

        var savedCard = await savedCardService.SaveCardAsync(buyerId, PayOrderEndpoint.MapCard(request.Card)!);

        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = savedCard.Id,
            Card = SavedCardDto.FromEntity(savedCard)
        };
        return Results.Created($"api/payment-methods/{savedCard.Id}", response);
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDetailsRequest? Card { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }

    public int PaymentMethodId { get; set; }
    public SavedCardDto Card { get; set; } = new();
}

public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static SavedCardDto FromEntity(SavedCard card) => new()
    {
        PaymentMethodId = card.Id,
        Brand = card.Brand,
        LastDigits = card.LastDigits,
        Expiry = card.Expiry,
        CardholderName = card.CardholderName,
        CreatedAt = card.CreatedAt
    };
}
