using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;
using static Microsoft.eShopWeb.PublicApi.PaymentEndpoints.PaymentApiHelpers;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// POST /api/payment-methods — saves a card for the signed-in shopper (vaulted at PayPal). The response
/// identifies the saved card and describes it safely (brand, last four, expiry) — never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest>
{
    private readonly IPaymentMethodService _paymentMethodService;

    public SavePaymentMethodEndpoint(IPaymentMethodService paymentMethodService) => _paymentMethodService = paymentMethodService;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user) =>
            {
                request.BuyerId = user.GetUserName() ?? string.Empty;
                return await HandleAsync(request);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request)
    {
        var card = new CardModel
        {
            Number = request.Number,
            Expiry = request.Expiry,
            SecurityCode = request.SecurityCode,
            CardholderName = request.CardholderName,
            BillingAddress = request.BillingAddress
        }.ToCardDetails()!;

        var result = await _paymentMethodService.SaveCardAsync(request.BuyerId, card);
        return ToHttp(result, saved => Results.Created($"api/payment-methods/{saved.PaymentMethodId}", new SavePaymentMethodResponse
        {
            PaymentMethodId = saved.PaymentMethodId,
            Brand = saved.Brand,
            LastFourDigits = saved.LastFourDigits,
            Expiry = saved.Expiry,
            CardholderName = saved.CardholderName
        }));
    }
}
