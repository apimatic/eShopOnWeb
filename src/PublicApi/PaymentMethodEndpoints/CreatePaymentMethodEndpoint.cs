using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper via PayPal's vault. Only safe-display fields
/// (brand, last digits, expiry) are kept; full card details are never stored.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ClaimsPrincipal>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public CreatePaymentMethodEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity!.Name!;

        var card = new GatewayCardDetails(
            request.Card.Number,
            request.Card.Expiry,
            request.Card.SecurityCode,
            request.Card.Name,
            request.Card.BillingAddress is null
                ? null
                : new GatewayBillingAddress(
                    request.Card.BillingAddress.AddressLine1,
                    request.Card.BillingAddress.AddressLine2,
                    request.Card.BillingAddress.City,
                    request.Card.BillingAddress.State,
                    request.Card.BillingAddress.PostalCode,
                    request.Card.BillingAddress.CountryCode));

        var savedCard = await _orderPaymentService.SaveCardAsync(buyerId, card);

        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = savedCard.Id,
            Brand = savedCard.Brand,
            LastDigits = savedCard.LastDigits,
            Expiry = savedCard.Expiry,
            CardholderName = savedCard.CardholderName
        };
        return Results.Created($"api/payment-methods/{savedCard.Id}", response);
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDetailsDto Card { get; set; } = new CardDetailsDto();
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) {}
    public CreatePaymentMethodResponse() {}

    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}
