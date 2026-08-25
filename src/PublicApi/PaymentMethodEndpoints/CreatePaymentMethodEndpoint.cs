using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequestBody
{
    public string? Alias { get; set; }
    public CardDto Card { get; set; } = new();
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public string? Alias { get; set; }
    public CardDto Card { get; set; } = new();
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }

    public int PaymentMethodId { get; set; }
    public string? Alias { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? CardType { get; set; }
}

/// <summary>
/// Saves a card for the signed-in shopper via PayPal's vault. Full card details are never stored by this app.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequestBody body, HttpContext httpContext, ISavedPaymentMethodService paymentMethodService) =>
            {
                var request = new CreatePaymentMethodRequest
                {
                    BuyerId = httpContext.User.Identity!.Name!,
                    Alias = body.Alias,
                    Card = body.Card
                };
                return await HandleAsync(request, paymentMethodService);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService paymentMethodService)
    {
        var response = new CreatePaymentMethodResponse(request.CorrelationId());

        var card = new PayPalCardDetails
        {
            Number = request.Card.Number,
            CardholderName = request.Card.CardholderName,
            ExpiryMonth = request.Card.ExpiryMonth,
            ExpiryYear = request.Card.ExpiryYear,
            SecurityCode = request.Card.SecurityCode,
            BillingAddress = request.Card.BillingAddress is null ? null : new PayPalBillingAddress
            {
                AddressLine1 = request.Card.BillingAddress.AddressLine1,
                AddressLine2 = request.Card.BillingAddress.AddressLine2,
                AdminArea1 = request.Card.BillingAddress.AdminArea1,
                AdminArea2 = request.Card.BillingAddress.AdminArea2,
                PostalCode = request.Card.BillingAddress.PostalCode,
                CountryCode = request.Card.BillingAddress.CountryCode
            }
        };

        var paymentMethod = await paymentMethodService.SavePaymentMethodAsync(request.BuyerId, card, request.Alias);

        response.PaymentMethodId = paymentMethod.Id;
        response.Alias = paymentMethod.Alias;
        response.Brand = paymentMethod.Brand;
        response.Last4 = paymentMethod.Last4;
        response.Expiry = paymentMethod.Expiry;
        response.CardType = paymentMethod.CardType;
        return Results.Created($"api/payment-methods/{paymentMethod.Id}", response);
    }
}
