using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/payment-methods — save (vault) a card for the signed-in shopper. The response identifies the
/// saved card and describes it safely (brand + last four); full card details are never stored or returned.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, HttpContext http)
    {
        try
        {
            var buyerId = PaymentEndpointHelpers.GetBuyerId(http);
            var svc = http.RequestServices.GetRequiredService<ISavedCardService>();

            var card = new CardInput
            {
                Number = request.Number,
                Expiry = request.Expiry,
                SecurityCode = request.SecurityCode,
                CardholderName = request.CardholderName,
                BillingAddressLine1 = request.BillingAddressLine1,
                BillingCity = request.BillingCity,
                BillingState = request.BillingState,
                BillingPostalCode = request.BillingPostalCode,
                BillingCountryCode = request.BillingCountryCode,
            };

            var saved = await svc.SaveCardAsync(buyerId, card, http.RequestAborted);
            return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", new CreatePaymentMethodResponse
            {
                PaymentMethodId = saved.PaymentMethodId,
                CardBrand = saved.CardBrand,
                LastFourDigits = saved.LastFourDigits,
                Expiry = saved.Expiry,
                CardholderName = saved.CardholderName,
            });
        }
        catch (Exception ex) when (ex is PaymentOperationException or PaymentGatewayException)
        {
            return PaymentEndpointHelpers.MapError(ex);
        }
    }
}

public class CreatePaymentMethodRequest
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry in YYYY-MM.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }
}

public class CreatePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? CardBrand { get; set; }
    public string? LastFourDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}
