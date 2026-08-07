using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest : BaseRequest
{
    public CardDto Card { get; set; } = new();

    /// <summary>Optional shopper-supplied label, e.g. "Personal Visa".</summary>
    public string? Alias { get; set; }
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public SavePaymentMethodResponse() { }

    /// <summary>Top-level identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }
    public SavedCardDto PaymentMethod { get; set; } = new();
}

/// <summary>Saves (vaults) a card for the signed-in shopper and returns a safe description of it.</summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ClaimsPrincipal, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService paymentMethodService) =>
            {
                return await HandleAsync(request, user, paymentMethodService);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService paymentMethodService)
    {
        if (!user.TryGetBuyerId(out var buyerId))
        {
            return Results.Unauthorized();
        }

        var validationError = ValidateCard(request.Card);
        if (validationError is not null)
        {
            return Results.BadRequest(new { message = validationError });
        }

        try
        {
            var card = PaymentApiMappings.ToCardDetails(request.Card);
            var paymentMethod = await paymentMethodService.SaveCardAsync(buyerId, card, request.Alias);

            var response = new SavePaymentMethodResponse(request.CorrelationId())
            {
                PaymentMethodId = paymentMethod.Id,
                PaymentMethod = PaymentApiMappings.ToSavedCard(paymentMethod)
            };
            return Results.Created($"api/payment-methods/{paymentMethod.Id}", response);
        }
        catch (Exception ex) when (ex.IsHandledPaymentException())
        {
            return ex.ToProblemResult();
        }
    }

    private static string? ValidateCard(CardDto? card)
    {
        if (card is null)
        {
            return "Card details are required.";
        }

        if (string.IsNullOrWhiteSpace(card.Number))
        {
            return "Card number is required.";
        }

        if (card.ExpiryMonth is < 1 or > 12)
        {
            return "Card expiry month must be between 1 and 12.";
        }

        if (card.ExpiryYear <= 0)
        {
            return "Card expiry year is required.";
        }

        if (string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            return "Card security code is required.";
        }

        if (card.BillingAddress is null || string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
        {
            return "A billing address with a country code is required.";
        }

        return null;
    }
}
