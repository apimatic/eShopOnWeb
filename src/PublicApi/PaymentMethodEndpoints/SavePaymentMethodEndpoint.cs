using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request, ISavedCardService service, HttpContext ctx) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                if (string.IsNullOrEmpty(request.CardNumber))
                    return Results.BadRequest(new { error = "cardNumber is required." });
                if (request.CardExpiryMonth < 1 || request.CardExpiryMonth > 12)
                    return Results.BadRequest(new { error = "cardExpiryMonth must be 1-12." });
                if (request.CardExpiryYear < DateTimeOffset.UtcNow.Year)
                    return Results.BadRequest(new { error = "Card is expired." });
                if (string.IsNullOrEmpty(request.BillingCountryCode))
                    return Results.BadRequest(new { error = "billingCountryCode is required." });

                try
                {
                    var saveRequest = new SaveCardRequest(
                        CardNumber: request.CardNumber,
                        CardExpiryMonth: request.CardExpiryMonth,
                        CardExpiryYear: request.CardExpiryYear,
                        Cvv: request.CardCvv,
                        CardholderName: request.CardholderName,
                        BillingCountryCode: request.BillingCountryCode,
                        BillingPostalCode: request.BillingPostalCode);

                    var result = await service.SaveCardAsync(buyerId, saveRequest);
                    return Results.Created(
                        $"api/payment-methods/{result.PaymentMethodId}",
                        new SavePaymentMethodResponse
                        {
                            PaymentMethodId = result.PaymentMethodId,
                            LastFour = result.LastFour,
                            Brand = result.Brand,
                            Expiry = result.Expiry,
                            CardholderName = result.CardholderName
                        });
                }
                catch (Infrastructure.Services.PayPal.PayPalException ex)
                    when ((int)ex.StatusCode >= 400 && (int)ex.StatusCode < 500)
                {
                    return Results.BadRequest(new { error = ex.Message, detail = ex.ResponseBody });
                }
            })
            .Produces<SavePaymentMethodResponse>(201)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService service)
        => await Task.FromResult(Results.StatusCode(501));
}
