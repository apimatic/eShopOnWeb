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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper. The card is vaulted with PayPal; only safe
/// display data (brand, last digits, expiry) is kept by the app.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, ISavedPaymentMethodService paymentMethodService) =>
            {
                request.Username = OrderMapping.GetUserName(user);
                return await HandleAsync(request, paymentMethodService);
            })
            .Produces<SavePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedPaymentMethodService paymentMethodService)
    {
        if (string.IsNullOrEmpty(request.Username))
        {
            return Results.Unauthorized();
        }
        if (request.Card == null || string.IsNullOrWhiteSpace(request.Card.Number) || string.IsNullOrWhiteSpace(request.Card.Expiry))
        {
            return Results.BadRequest(new SavePaymentMethodResponse { Message = "card.number and card.expiry (YYYY-MM) are required." });
        }

        try
        {
            var saved = await paymentMethodService.SaveCardAsync(request.Username, MapCard(request.Card));
            return Results.Ok(ToResponse(saved));
        }
        catch (PaymentException ex)
        {
            return Results.UnprocessableEntity(new SavePaymentMethodResponse { Message = ex.Message });
        }
    }

    internal static SavePaymentMethodResponse ToResponse(SavedPaymentMethod saved) => new()
    {
        PaymentMethodId = saved.Id,
        Brand = saved.Brand,
        LastDigits = saved.LastDigits,
        Expiry = saved.Expiry,
        CreatedAt = saved.CreatedAt
    };

    private static CardDetails MapCard(CardRequest card) => new()
    {
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        CardholderName = card.CardholderName,
        BillingAddressLine1 = card.BillingAddressLine1,
        BillingAddressLine2 = card.BillingAddressLine2,
        BillingCity = card.BillingCity,
        BillingState = card.BillingState,
        BillingPostalCode = card.BillingPostalCode,
        BillingCountryCode = card.BillingCountryCode
    };
}

public class SavePaymentMethodRequest : BaseRequest
{
    [JsonIgnore]
    public string? Username { get; set; }

    public CardRequest? Card { get; set; }
}

public class SavePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Message { get; set; }
}
