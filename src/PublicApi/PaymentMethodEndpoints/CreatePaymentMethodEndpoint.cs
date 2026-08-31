using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved
/// card and carries only safe display data - never full card details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ISavedPaymentMethodService savedPaymentMethodService, HttpContext httpContext) =>
            {
                request.BuyerId = httpContext.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
                return await HandleAsync(request, savedPaymentMethodService);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService savedPaymentMethodService)
    {
        if (request.Card == null)
        {
            return Results.BadRequest(new { message = "card is required." });
        }

        var response = new CreatePaymentMethodResponse(request.CorrelationId());

        var card = new CardDetails(
            request.Card.Number,
            request.Card.Expiry,
            request.Card.SecurityCode,
            request.Card.CardholderName,
            request.Card.BillingAddress == null
                ? null
                : new CardBillingAddress(
                    request.Card.BillingAddress.Line1,
                    request.Card.BillingAddress.Line2,
                    request.Card.BillingAddress.City,
                    request.Card.BillingAddress.State,
                    request.Card.BillingAddress.PostalCode,
                    request.Card.BillingAddress.CountryCode));

        var saved = await savedPaymentMethodService.SaveCardAsync(request.BuyerId, card);

        response.PaymentMethodId = saved.Id;
        response.PaymentMethod = PaymentMethodDto.FromSavedPaymentMethod(saved);
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    public CardRequest? Card { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }

    public int PaymentMethodId { get; set; }
    public PaymentMethodDto? PaymentMethod { get; set; }
}
