using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    [JsonIgnore]
    public string? BuyerId { get; set; }

    public CardRequest Card { get; set; } = new();
}

public class PaymentMethodDto
{
    public int Id { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto FromEntity(SavedPaymentMethod entity) => new()
    {
        Id = entity.Id,
        Brand = entity.Brand,
        LastDigits = entity.LastDigits,
        Expiry = entity.Expiry,
        CardholderName = entity.CardholderName,
        CreatedAt = entity.CreatedAt
    };
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public PaymentMethodDto? PaymentMethod { get; set; }
}

/// <summary>
/// Saves a card for the signed-in shopper. The response identifies the saved card and
/// describes it only by safe fields (brand, last digits) — never full card details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ISavedCardService savedCardService, ClaimsPrincipal user) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, savedCardService);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedCardService savedCardService)
    {
        var response = new CreatePaymentMethodResponse(request.CorrelationId());

        if (request.BuyerId == null)
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.Card.Number) || string.IsNullOrWhiteSpace(request.Card.Expiry))
        {
            return Results.BadRequest("Card number and expiry are required.");
        }

        var saved = await savedCardService.SaveCardAsync(request.BuyerId, request.Card.ToCardDetails(), default);

        response.PaymentMethodId = saved.Id;
        response.PaymentMethod = PaymentMethodDto.FromEntity(saved);
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
