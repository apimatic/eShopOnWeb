using System.Security.Claims;
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

/// <summary>
/// Saves a card for the signed-in shopper. The response identifies the saved card and describes it
/// safely (brand + last four + expiry) — never full card details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.BuyerId = BuyerIdentity.GetBuyerId(user);
                return await HandleAsync(request, paymentService);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentService paymentService)
    {
        if (request.Card == null)
        {
            throw new PaymentException("Card details are required to save a payment method.", 400);
        }

        var saved = await paymentService.SaveCardAsync(request.BuyerId, request.Card.ToCardDetails());

        var response = new CreatePaymentMethodResponse
        {
            PaymentMethodId = saved.PaymentMethodId,
            Brand = saved.Brand,
            LastFourDigits = saved.LastFourDigits,
            Expiry = saved.Expiry
        };
        return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", response);
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDto? Card { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
}
