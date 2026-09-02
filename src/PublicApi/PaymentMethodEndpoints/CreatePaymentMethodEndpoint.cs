using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Helpers;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper via PayPal's vault. The response identifies
/// the saved card and shows only safe display data (brand, last digits, expiry).
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, HttpContext>
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
            (CreatePaymentMethodRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(request, httpContext);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, HttpContext httpContext)
    {
        var buyerId = httpContext.User.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }
        if (request.Card is null || string.IsNullOrWhiteSpace(request.Card.Number))
        {
            return Results.BadRequest(new { message = "Card details are required." });
        }

        try
        {
            var savedCard = await _savedCardService.SaveCardAsync(buyerId, request.Card.ToCardDetails());

            return Results.Created($"api/payment-methods/{savedCard.Id}", new CreatePaymentMethodResponse(request.CorrelationId())
            {
                PaymentMethodId = savedCard.Id,
                Brand = savedCard.Brand,
                LastDigits = savedCard.LastDigits,
                Expiry = savedCard.Expiry,
                CardholderName = savedCard.CardholderName
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return EndpointHelpers.MapException(ex);
        }
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
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}
