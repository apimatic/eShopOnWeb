using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class SavePaymentMethodRequest : BaseRequest
{
    public CardDto Card { get; set; } = new();

    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }

    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Saves a card for the signed-in shopper (vaulted at PayPal). The response identifies the saved card and
/// describes it safely — brand and last four digits — never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService service) =>
            {
                var buyerId = user.BuyerId();
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                request.BuyerId = buyerId;
                return await HandleAsync(request, service);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentMethodService service)
    {
        if (request.Card is null || string.IsNullOrWhiteSpace(request.Card.Number))
        {
            throw new PaymentValidationException("Card details are required to save a card.");
        }

        var saved = await service.SaveCardAsync(request.BuyerId, request.Card.ToCardDetails());
        var dto = saved.ToDto();

        return Results.Created($"api/payment-methods/{dto.PaymentMethodId}",
            new SavePaymentMethodResponse(request.CorrelationId())
            {
                PaymentMethodId = dto.PaymentMethodId,
                Brand = dto.Brand,
                LastFourDigits = dto.LastFourDigits,
                Expiry = dto.Expiry,
                CardholderName = dto.CardholderName,
                CreatedAt = dto.CreatedAt
            });
    }
}
