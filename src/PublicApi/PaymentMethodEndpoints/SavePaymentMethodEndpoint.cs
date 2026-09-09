using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved card and
/// describes it safely (brand + last four) — never full card details, which are not stored.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.BuyerId = PaymentMapping.GetBuyerId(user);
                return await HandleAsync(request, paymentService);
            })
            .Produces<SavePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentService paymentService)
    {
        var response = new SavePaymentMethodResponse(request.CorrelationId());

        if (request.Card is null)
        {
            throw new PaymentValidationException("Card details are required to save a card.");
        }

        var result = await paymentService.SaveCardAsync(request.BuyerId, request.Card.ToGatewayCard());

        response.PaymentMethodId = result.PaymentMethodId;
        response.Brand = result.Card.Brand;
        response.LastFourDigits = result.Card.LastFourDigits;
        response.Expiry = result.Card.Expiry;
        response.CardholderName = result.Card.CardholderName;
        return Results.Created($"api/payment-methods/{result.PaymentMethodId}", response);
    }
}

public class SavePaymentMethodRequest : BaseRequest
{
    public CardRequestDto? Card { get; set; }
    internal string BuyerId { get; set; } = string.Empty;
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public SavePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
}
